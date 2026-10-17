using StackExchange.Redis;
using StangaNetLib.Caching.Configuration;
using StangaNetLib.Core.Common;
using StangaNetLib.Resilience.Execution;

namespace StangaNetLib.Caching.Locking;

/// <summary>
/// Distributed lock backed by Redis using SET NX with atomic Lua release.
/// Safe for multi-node deployments. Non-reentrant.
/// When an <see cref="IResilientExecutor"/> is provided, the lock-acquire command is wrapped
/// in the <c>"Caching"</c> resilience pipeline. Transient Redis failures return <c>null</c>
/// (lock not acquired) rather than propagating exceptions to the caller.
/// </summary>
public sealed class RedisDistributedLockService : IDistributedLockService
{
    /// <summary>Resilience pipeline name consumed from <c>ResilienceSettings.Pipelines</c> when <see cref="IResilientExecutor"/> is active.</summary>
    internal const string PipelineName = "Caching";

    private readonly IConnectionMultiplexer _redis;
    private readonly CacheSettings _settings;
    private readonly IResilientExecutor? _resilience;

    /// <summary>
    /// Initializes a new instance of <see cref="RedisDistributedLockService"/>.
    /// </summary>
    /// <param name="redis">The Redis connection multiplexer (singleton, thread-safe).</param>
    /// <param name="settings">Cache configuration used to apply the key prefix to lock keys.</param>
    /// <param name="resilience">
    /// Optional resilience executor. When provided, wraps the lock-acquire command in the <c>"Caching"</c>
    /// pipeline. On resilience failure the lock is treated as unavailable (<c>null</c> is returned).
    /// </param>
    public RedisDistributedLockService(
        IConnectionMultiplexer redis,
        CacheSettings settings,
        IResilientExecutor? resilience = null)
    {
        _redis = redis;
        _settings = settings;
        _resilience = resilience;
    }

    private IDatabase Db => _redis.GetDatabase();

    /// <inheritdoc/>
    /// <remarks>
    /// When <paramref name="waitTimeout"/> is <c>null</c> or <see cref="TimeSpan.Zero"/>, a single
    /// non-blocking SET NX attempt is made. A positive timeout polls with jitter until the lock
    /// is acquired or the deadline elapses.
    /// </remarks>
    public async Task<Result<IDistributedLockScope>> TryAcquireAsync(
        string resource,
        TimeSpan expiry,
        TimeSpan? waitTimeout = null,
        CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = ApplyPrefix($"lock:{resource}");
        var timeout = waitTimeout ?? TimeSpan.Zero;

        if (timeout <= TimeSpan.Zero)
            return await TryAcquireOnceAsync(db, key, expiry, cancellationToken);

        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var attempt = await TryAcquireOnceAsync(db, key, expiry, cancellationToken);
            if (attempt.IsSuccess)
                return attempt;

            var remaining = deadline - DateTimeOffset.UtcNow;
            var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(20, 81));
            var delay = remaining < jitter ? remaining : jitter;
            if (delay <= TimeSpan.Zero)
                break;

            await Task.Delay(delay, cancellationToken);
        }

        return Result<IDistributedLockScope>.Failure(Error.Conflict("Lock", "Resource is already locked"));
    }

    private async Task<Result<IDistributedLockScope>> TryAcquireOnceAsync(
        IDatabase db,
        string key,
        TimeSpan expiry,
        CancellationToken cancellationToken)
    {
        var token = Guid.NewGuid().ToString("N");

        bool acquired;
        if (_resilience is null)
        {
            acquired = await db.StringSetAsync(key, token, expiry, When.NotExists);
        }
        else
        {
            var r = await _resilience.ExecuteAsync<bool>(
                PipelineName,
                _ => db.StringSetAsync(key, token, expiry, When.NotExists),
                cancellationToken);

            if (!r.IsSuccess)
                return Result<IDistributedLockScope>.Failure(r.Error);

            acquired = r.Value;
        }

        return acquired
            ? Result<IDistributedLockScope>.Success(new LockScope(db, key, token))
            : Result<IDistributedLockScope>.Failure(Error.Conflict("Lock", "Resource is already locked"));
    }

    private string ApplyPrefix(string key) =>
        string.IsNullOrEmpty(_settings.KeyPrefix) ? key : $"{_settings.KeyPrefix}:{key}";

    private sealed class LockScope : IDistributedLockScope
    {
        private readonly IDatabase _db;
        private readonly string _key;
        private readonly string _token;
        private int _released;

        // Atomic release: delete the key only if the stored token matches ours.
        // Prevents releasing a lock re-acquired by another holder after our expiry.
        private const string ReleaseScript =
            "if redis.call('get', KEYS[1]) == ARGV[1] then " +
            "  return redis.call('del', KEYS[1]) " +
            "else " +
            "  return 0 " +
            "end";

        public LockScope(IDatabase db, string key, string token)
        {
            _db = db;
            _key = key;
            _token = token;
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0) return;
            await _db.ScriptEvaluateAsync(ReleaseScript,
                new RedisKey[] { _key },
                new RedisValue[] { _token });
        }
    }
}
