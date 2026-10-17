using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using StangaNetLib.Caching.Storage;
using StangaNetLib.Caching.Extensions;
using StangaNetLib.Caching.Configuration;
using StangaNetLib.Resilience.Execution;
using StangaNetLib.Caching.Locking;
using StangaNetLib.Core.Common;

namespace StangaNetLib.Caching.Storage;

/// <summary>
/// Distributed cache implementation backed by Redis via StackExchange.Redis.
/// Suitable for multi-node deployments. Supports prefix removal via SCAN.
/// <see cref="GetOrSetAsync{T}"/> is thread-safe: concurrent calls on the same key are serialized
/// via <see cref="IDistributedLockService"/> using a distributed lock with a 10-second wait timeout.
/// When an <see cref="IResilientExecutor"/> is registered in the DI container it is injected automatically
/// and wraps all Redis commands using the <c>"Caching"</c> resilience pipeline
/// (retry, circuit-breaker, timeout driven by <c>ResilienceSettings.Pipelines["Caching"]</c>).
/// On resilience failure (retry exhaustion, circuit open, timeout), read operations degrade to
/// a cache miss and write operations are silently discarded — the application continues to operate
/// against the source of truth without surfacing Redis exceptions to callers.
/// </summary>
public sealed class RedisCacheService : ICacheService
{
    /// <summary>Resilience pipeline name consumed from <c>ResilienceSettings.Pipelines</c> when <see cref="IResilientExecutor"/> is active.</summary>
    internal const string PipelineName = "Caching";

    private readonly IConnectionMultiplexer _redis;
    private readonly CacheSettings _settings;
    private readonly ILogger<RedisCacheService> _logger;
    private readonly IDistributedLockService _lockService;
    private readonly IResilientExecutor? _resilience;

    /// <summary>
    /// Initializes a new instance of <see cref="RedisCacheService"/>.
    /// </summary>
    /// <param name="redis">The Redis connection multiplexer (singleton, thread-safe).</param>
    /// <param name="settings">Cache configuration used to apply the key prefix to lock keys.</param>
    /// <param name="logger">Logger for trace and debug output.</param>
    /// <param name="lockService">
    /// Distributed lock service used by <see cref="GetOrSetAsync{T}"/> to prevent the thundering herd problem.
    /// </param>
    /// <param name="resilience">
    /// Optional resilience executor. When provided, all Redis commands are wrapped in the <c>"Caching"</c>
    /// pipeline. Register <c>AddStangaNetLibResilience</c> before <c>AddStangaNetLibCaching</c> and add a
    /// <c>"Caching"</c> entry to <c>ResilienceSettings.Pipelines</c> to configure retry, circuit-breaker,
    /// and timeout behaviour for Redis operations.
    /// </param>
    public RedisCacheService(
        IConnectionMultiplexer redis,
        CacheSettings settings,
        ILogger<RedisCacheService> logger,
        IDistributedLockService lockService,
        IResilientExecutor? resilience = null)
    {
        _redis = redis;
        _settings = settings;
        _logger = logger;
        _lockService = lockService;
        _resilience = resilience;
    }

    private IDatabase Db => _redis.GetDatabase();

    /// <inheritdoc/>
    public async Task<Result<T?>> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
    {
        var fullKey = ApplyPrefix(key);

        RedisValue json;
        if (_resilience is null)
        {
            json = await Db.StringGetAsync(fullKey);
        }
        else
        {
            var r = await _resilience.ExecuteAsync<RedisValue>(
                PipelineName,
                _ => Db.StringGetAsync(fullKey),
                cancellationToken);

            if (!r.IsSuccess)
            {
                _logger.LogWarning("Redis GetAsync degraded (key: {Key}): {Error}. Cache miss returned.", fullKey, r.Error);
                return Result<T?>.Success(null);
            }

            json = r.Value;
        }

        if (json.IsNullOrEmpty)
        {
            _logger.LogTrace("Cache MISS (Redis): {Key}", fullKey);
            return Result<T?>.Success(null);
        }

        _logger.LogTrace("Cache HIT (Redis): {Key}", fullKey);
        return Result<T?>.Success(JsonSerializer.Deserialize<T>((string)json!, _settings.SerializerOptions));
    }

    /// <inheritdoc/>
    public async Task<Result> SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default) where T : class
    {
        var fullKey = ApplyPrefix(key);
        var json = JsonSerializer.Serialize(value, _settings.SerializerOptions);
        var expiry = expiration ?? _settings.DefaultExpiry;

        if (_resilience is null)
        {
            await Db.StringSetAsync(fullKey, json, expiry);
        }
        else
        {
            var r = await _resilience.ExecuteAsync<bool>(
                PipelineName,
                _ => Db.StringSetAsync(fullKey, json, expiry),
                cancellationToken);

            if (!r.IsSuccess)
            {
                _logger.LogWarning("Redis SetAsync degraded (key: {Key}): {Error}. Entry not cached.", fullKey, r.Error);
                return Result.Failure(r.Error);
            }
        }

        _logger.LogTrace("Cache SET (Redis): {Key} (expiry: {Expiry})", fullKey, expiry);
        return Result.Success();
    }

    /// <inheritdoc/>
    public async Task<Result> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        var fullKey = ApplyPrefix(key);

        if (_resilience is null)
        {
            await Db.KeyDeleteAsync(fullKey);
        }
        else
        {
            var r = await _resilience.ExecuteAsync<bool>(
                PipelineName,
                _ => Db.KeyDeleteAsync(fullKey),
                cancellationToken);

            if (!r.IsSuccess)
            {
                _logger.LogWarning("Redis RemoveAsync degraded (key: {Key}): {Error}. Entry not removed.", fullKey, r.Error);
                return Result.Failure(r.Error);
            }
        }

        _logger.LogTrace("Cache REMOVE (Redis): {Key}", fullKey);
        return Result.Success();
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        var fullKey = ApplyPrefix(key);

        if (_resilience is null)
            return Result<bool>.Success(await Db.KeyExistsAsync(fullKey));

        var r = await _resilience.ExecuteAsync<bool>(
            PipelineName,
            _ => Db.KeyExistsAsync(fullKey),
            cancellationToken);

        if (!r.IsSuccess)
        {
            _logger.LogWarning("Redis ExistsAsync degraded (key: {Key}): {Error}. Returning false.", fullKey, r.Error);
            return Result<bool>.Success(false);
        }

        return Result<bool>.Success(r.Value);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Uses a distributed lock (via <see cref="IDistributedLockService"/>) to prevent the thundering herd problem:
    /// concurrent callers with the same key are serialized and the factory is invoked at most once.
    /// If the lock cannot be acquired within 10 seconds, the factory is called anyway (best-effort fallback)
    /// and a warning is logged so operators can detect lock contention or connectivity issues.
    /// </remarks>
    public async Task<Result<T>> GetOrSetAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default) where T : class
    {
        var cachedResult = await GetAsync<T>(key, cancellationToken);
        if (cachedResult.IsSuccess && cachedResult.Value != null)
            return Result<T>.Success(cachedResult.Value);

        var lockResult = await _lockService.TryAcquireAsync(
            resource: $"getorset:{key}",
            expiry: TimeSpan.FromSeconds(30),
            waitTimeout: TimeSpan.FromSeconds(10),
            cancellationToken);

        if (lockResult.IsFailure)
        {
            _logger.LogWarning(
                "GetOrSetAsync: could not acquire distributed lock for key '{Key}' within 10 s. " +
                "Proceeding without lock — factory may be called by multiple nodes concurrently.",
                ApplyPrefix(key));
        }

        if (lockResult.IsSuccess)
        {
            await using (lockResult.Value!)
            {
                // Double-check after acquiring the lock — another node may have populated the key.
                var doubleCheck = await GetAsync<T>(key, cancellationToken);
                if (doubleCheck.IsSuccess && doubleCheck.Value != null)
                    return Result<T>.Success(doubleCheck.Value);

                try
                {
                    var value = await factory(cancellationToken);
                    if (value != null)
                        await SetAsync(key, value, expiration, cancellationToken);
                    return Result<T>.Success(value!);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during cache-aside factory execution for key: {Key}", key);
                    return Result<T>.Failure(Error.Internal(ex.Message));
                }
            }
        }
        else
        {
            // Fallback: execute factory without lock.
            try
            {
                var value = await factory(cancellationToken);
                if (value != null)
                    await SetAsync(key, value, expiration, cancellationToken);
                return Result<T>.Success(value!);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during cache-aside factory execution for key: {Key} (unlocked)", key);
                return Result<T>.Failure(Error.Internal(ex.Message));
            }
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Uses Redis SCAN to find all keys matching the prefix pattern, then deletes them in a single batch.
    /// SCAN is non-blocking but may take multiple round-trips on large keyspaces.
    /// In Redis Cluster mode, SCAN runs only per-node that handles the pattern's hash slot.
    /// For cluster deployments with many nodes, consider using a TTL-based invalidation strategy instead.
    /// </remarks>
    public async Task<Result> RemoveByPrefixAsync(string keyPrefix, CancellationToken cancellationToken = default)
    {
        var fullPrefix = ApplyPrefix(keyPrefix);
        var pattern = $"{fullPrefix}*";

        var primaryServers = _redis.GetEndPoints()
            .Select(ep => _redis.GetServer(ep))
            .Where(s => !s.IsReplica && s.IsConnected)
            .ToList();

        if (primaryServers.Count > 1)
            _logger.LogWarning(
                "RemoveByPrefixAsync: {Count} primary nodes detected. In Redis Cluster, SCAN is per-node — " +
                "keys distributed across other hash slots will not be removed. Consider TTL-based invalidation for cluster deployments.",
                primaryServers.Count);

        var allKeys = new List<RedisKey>();
        foreach (var server in primaryServers)
        {
            await foreach (var k in server.KeysAsync(pattern: pattern, pageSize: 250))
                allKeys.Add(k);
        }

        if (allKeys.Count == 0)
        {
            _logger.LogDebug("Cache RemoveByPrefix (Redis): no entries matched prefix '{Prefix}'", fullPrefix);
            return Result.Success();
        }

        var redisKeys = allKeys.ToArray();

        if (_resilience is null)
        {
            await Db.KeyDeleteAsync(redisKeys);
        }
        else
        {
            var r = await _resilience.ExecuteAsync<long>(
                PipelineName,
                _ => Db.KeyDeleteAsync(redisKeys),
                cancellationToken);

            if (!r.IsSuccess)
            {
                _logger.LogWarning("Redis RemoveByPrefixAsync degraded (prefix: {Prefix}): {Error}. Entries not removed.", fullPrefix, r.Error);
                return Result.Failure(r.Error);
            }
        }

        _logger.LogDebug("Cache RemoveByPrefix (Redis): removed {Count} entries with prefix '{Prefix}'",
            allKeys.Count, fullPrefix);

        return Result.Success();
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> ScanKeysAsync(
        string pattern,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var fullPattern = ApplyPrefix(pattern);
        var prefixStrip = PrefixStripLength();

        foreach (var endpoint in _redis.GetEndPoints())
        {
            var server = _redis.GetServer(endpoint);
            if (server.IsReplica) continue;

            await foreach (var key in server.KeysAsync(pattern: fullPattern, pageSize: 250).WithCancellation(cancellationToken))
            {
                var strKey = key.ToString();
                yield return strKey[prefixStrip..];
            }
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Uses Redis <c>MGET</c> to retrieve all values in a single round-trip.
    /// </remarks>
    public async Task<Result<IReadOnlyDictionary<string, T?>>> GetManyAsync<T>(
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default) where T : class
    {
        var keyList = keys.ToList();
        if (keyList.Count == 0)
            return Result<IReadOnlyDictionary<string, T?>>.Success(new Dictionary<string, T?>());

        var fullKeys = keyList.Select(k => (RedisKey)ApplyPrefix(k)).ToArray();

        RedisValue[] values;
        if (_resilience is null)
        {
            values = await Db.StringGetAsync(fullKeys);
        }
        else
        {
            var r = await _resilience.ExecuteAsync<RedisValue[]>(
                PipelineName,
                _ => Db.StringGetAsync(fullKeys),
                cancellationToken);

            if (!r.IsSuccess)
            {
                _logger.LogWarning("Redis GetManyAsync degraded: {Error}. Returning all cache misses.", r.Error);
                return Result<IReadOnlyDictionary<string, T?>>.Success(keyList.ToDictionary(k => k, _ => (T?)null));
            }

            // r.Value is guaranteed non-null: StackExchange.Redis StringGetAsync(keys[]) never returns null.
            values = r.Value!;
        }

        var result = new Dictionary<string, T?>(keyList.Count);
        for (var i = 0; i < keyList.Count; i++)
        {
            result[keyList[i]] = values[i].IsNullOrEmpty
                ? null
                : JsonSerializer.Deserialize<T>((string)values[i]!, _settings.SerializerOptions);
        }
        return Result<IReadOnlyDictionary<string, T?>>.Success(result);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Pipelines all <c>SET … EX</c> commands via an <c>IBatch</c> — single round-trip flush to Redis.
    /// Batch operations cannot be retried atomically; transient Redis failures surface as exceptions
    /// on individual tasks returned by <c>Task.WhenAll</c>.
    /// </remarks>
    public async Task<Result> SetManyAsync<T>(
        IEnumerable<KeyValuePair<string, T>> entries,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default) where T : class
    {
        var expiry = expiration ?? _settings.DefaultExpiry;
        var db = Db;
        var batch = db.CreateBatch();
        var tasks = new List<Task>();

        foreach (var (key, value) in entries)
        {
            var fullKey = ApplyPrefix(key);
            var json = JsonSerializer.Serialize(value, _settings.SerializerOptions);
            tasks.Add(batch.StringSetAsync(fullKey, json, expiry));
        }

        if (tasks.Count == 0) return Result.Success();

        batch.Execute();
        await Task.WhenAll(tasks).WaitAsync(cancellationToken);
        _logger.LogDebug("Cache SetMany (Redis): stored {Count} entries", tasks.Count);
        return Result.Success();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Uses the multi-key Redis <c>DEL</c> overload — one round-trip regardless of key count.
    /// </remarks>
    public async Task<Result> RemoveManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        var redisKeys = keys.Select(k => (RedisKey)ApplyPrefix(k)).ToArray();
        if (redisKeys.Length == 0) return Result.Success();

        if (_resilience is null)
        {
            var deleted = await Db.KeyDeleteAsync(redisKeys);
            _logger.LogDebug("Cache RemoveMany (Redis): deleted {Count}/{Total} keys", deleted, redisKeys.Length);
            return Result.Success();
        }

        var r = await _resilience.ExecuteAsync<long>(
            PipelineName,
            _ => Db.KeyDeleteAsync(redisKeys),
            cancellationToken);

        if (!r.IsSuccess)
        {
            _logger.LogWarning("Redis RemoveManyAsync degraded: {Error}. Entries not removed.", r.Error);
            return Result.Failure(r.Error);
        }

        _logger.LogDebug("Cache RemoveMany (Redis): deleted {Count}/{Total} keys", r.Value, redisKeys.Length);
        return Result.Success();
    }

    private string ApplyPrefix(string key) =>
        string.IsNullOrEmpty(_settings.KeyPrefix) ? key : $"{_settings.KeyPrefix}:{key}";

    private int PrefixStripLength() =>
        string.IsNullOrEmpty(_settings.KeyPrefix) ? 0 : _settings.KeyPrefix.Length + 1;
}
