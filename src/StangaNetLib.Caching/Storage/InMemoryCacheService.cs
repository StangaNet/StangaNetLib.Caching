using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using StangaNetLib.Caching.Storage;
using StangaNetLib.Caching.Configuration;
using StangaNetLib.Concurrency.Locking;
using StangaNetLib.Core.Common;

namespace StangaNetLib.Caching.Storage;

/// <summary>
/// In-process cache implementation backed by <see cref="IMemoryCache"/>.
/// Suitable for single-node deployments. All data is lost on application restart.
/// Supports prefix removal via an internal key registry.
/// <see cref="GetOrSetAsync{T}"/> is thread-safe: concurrent calls on the same key are serialized via a per-key lock.
/// </summary>
public sealed class InMemoryCacheService : ICacheService
{
    private readonly IMemoryCache _memoryCache;
    private readonly CacheSettings _settings;
    private readonly ILogger<InMemoryCacheService> _logger;
    private readonly IKeyedLock _keyedLock;

    // Tracks all active keys to support RemoveByPrefixAsync and ScanKeysAsync.
    private readonly ConcurrentDictionary<string, byte> _keyRegistry = new();

    /// <summary>
    /// Initializes a new instance of <see cref="InMemoryCacheService"/>.
    /// </summary>
    /// <param name="memoryCache">The underlying in-process memory cache.</param>
    /// <param name="settings">Cache configuration (key prefix, default expiry, serializer options).</param>
    /// <param name="logger">Logger for trace and debug output.</param>
    /// <param name="keyedLockFactory">
    /// Factory used to create an isolated per-key lock pool for this service.
    /// Each service instance gets its own lock namespace to avoid cross-service key collisions.
    /// </param>
    public InMemoryCacheService(
        IMemoryCache memoryCache,
        CacheSettings settings,
        ILogger<InMemoryCacheService> logger,
        IKeyedLockFactory keyedLockFactory)
    {
        _memoryCache = memoryCache;
        _settings = settings;
        _logger = logger;
        _keyedLock = keyedLockFactory.Create();
    }

    /// <inheritdoc/>
    public Task<Result<T?>> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
    {
        var fullKey = ApplyPrefix(key);

        if (!_memoryCache.TryGetValue(fullKey, out string? json) || string.IsNullOrEmpty(json))
        {
            _logger.LogTrace("Cache MISS: {Key}", fullKey);
            return Task.FromResult(Result<T?>.Success(null));
        }

        _logger.LogTrace("Cache HIT: {Key}", fullKey);
        return Task.FromResult(Result<T?>.Success(JsonSerializer.Deserialize<T>(json, _settings.SerializerOptions)));
    }

    /// <inheritdoc/>
    public Task<Result> SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default) where T : class
    {
        var fullKey = ApplyPrefix(key);
        var json = JsonSerializer.Serialize(value, _settings.SerializerOptions);
        var expiry = expiration ?? _settings.DefaultExpiry;

        var options = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = expiry
        };

        options.RegisterPostEvictionCallback((evictedKey, _, _, _) =>
            _keyRegistry.TryRemove(evictedKey.ToString()!, out _));

        _memoryCache.Set(fullKey, json, options);
        _keyRegistry.TryAdd(fullKey, 0);

        _logger.LogTrace("Cache SET: {Key} (expiry: {Expiry})", fullKey, expiry);
        return Task.FromResult(Result.Success());
    }

    /// <inheritdoc/>
    public Task<Result> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        var fullKey = ApplyPrefix(key);
        _memoryCache.Remove(fullKey);
        _keyRegistry.TryRemove(fullKey, out _);
        _logger.LogTrace("Cache REMOVE: {Key}", fullKey);
        return Task.FromResult(Result.Success());
    }

    /// <inheritdoc/>
    public Task<Result<bool>> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        var fullKey = ApplyPrefix(key);
        return Task.FromResult(Result<bool>.Success(_memoryCache.TryGetValue(fullKey, out _)));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Uses a per-key <see cref="IKeyedLock"/> to prevent the thundering herd problem:
    /// concurrent callers with the same key are serialized and the factory is invoked at most once.
    /// </remarks>
    public async Task<Result<T>> GetOrSetAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default) where T : class
    {
        var cachedResult = await GetAsync<T>(key, cancellationToken);
        if (cachedResult.IsSuccess && cachedResult.Value != null)
        {
            return Result<T>.Success(cachedResult.Value);
        }

        var lockResult = await _keyedLock.TryAcquireAsync(key, TimeSpan.FromSeconds(30), cancellationToken);
        if (lockResult.IsSuccess)
        {
            await using var handle = lockResult.Value;
            // Double-check after acquiring the lock — another thread may have populated the key.
            var doubleCheckResult = await GetAsync<T>(key, cancellationToken);
            if (doubleCheckResult.IsSuccess && doubleCheckResult.Value != null)
                return Result<T>.Success(doubleCheckResult.Value);

            try
            {
                var value = await factory(cancellationToken);
                if (value != null)
                {
                    await SetAsync(key, value, expiration, cancellationToken);
                    return Result<T>.Success(value);
                }

                return Result<T>.Success(null!);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during cache-aside factory execution for key: {Key}", key);
                return Result<T>.Failure(Error.Internal(ex.Message));
            }
        }

        return Result<T>.Failure(lockResult.Error!);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Iterates the internal key registry and removes all matching entries.
    /// Use <see cref="Keys.CacheKeyBuilder.BuildPrefix"/> to build a prefix with a trailing colon
    /// and avoid partial matches on adjacent keys.
    /// </remarks>
    public Task<Result> RemoveByPrefixAsync(string keyPrefix, CancellationToken cancellationToken = default)
    {
        var fullPrefix = ApplyPrefix(keyPrefix);
        var keysToRemove = _keyRegistry.Keys
            .Where(k => k.StartsWith(fullPrefix, StringComparison.Ordinal))
            .ToList();

        foreach (var key in keysToRemove)
        {
            _memoryCache.Remove(key);
            _keyRegistry.TryRemove(key, out _);
        }

        _logger.LogDebug("Cache RemoveByPrefix: removed {Count} entries with prefix '{Prefix}'",
            keysToRemove.Count, fullPrefix);

        return Task.FromResult(Result.Success());
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> ScanKeysAsync(
        string pattern,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var fullPattern = ApplyPrefix(pattern);
        var regex = BuildGlobRegex(fullPattern);
        var prefixLen = PrefixStripLength();

        foreach (var key in _keyRegistry.Keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (regex.IsMatch(key))
                yield return key[prefixLen..];
        }

        await Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyDictionary<string, T?>>> GetManyAsync<T>(
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default) where T : class
    {
        var result = new Dictionary<string, T?>();
        foreach (var key in keys)
        {
            var getResult = await GetAsync<T>(key, cancellationToken);
            if (getResult.IsSuccess)
                result[key] = getResult.Value;
            else
                result[key] = null; // If one fails, we treat it as null for the batch, or should we fail the whole batch?
                // For consistency with original behavior:
        }
        return Result<IReadOnlyDictionary<string, T?>>.Success(result);
    }

    /// <inheritdoc/>
    public async Task<Result> SetManyAsync<T>(
        IEnumerable<KeyValuePair<string, T>> entries,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default) where T : class
    {
        foreach (var (key, value) in entries)
            await SetAsync(key, value, expiration, cancellationToken);

        return Result.Success();
    }

    /// <inheritdoc/>
    public async Task<Result> RemoveManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        foreach (var key in keys)
            await RemoveAsync(key, cancellationToken);

        return Result.Success();
    }

    private string ApplyPrefix(string key) =>
        string.IsNullOrEmpty(_settings.KeyPrefix) ? key : $"{_settings.KeyPrefix}:{key}";

    private int PrefixStripLength() =>
        string.IsNullOrEmpty(_settings.KeyPrefix) ? 0 : _settings.KeyPrefix.Length + 1;

    private static Regex BuildGlobRegex(string pattern)
    {
        var escaped = Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".");
        return new Regex($"^{escaped}$", RegexOptions.Compiled);
    }
}
