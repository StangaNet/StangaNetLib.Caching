using StangaNetLib.Core.Common;
using StangaNetLib.Caching.Configuration;

namespace StangaNetLib.Caching.Storage;

/// <summary>
/// Unified cache abstraction for Clean Architecture.
/// Provides get, set, remove, existence check, cache-aside shorthand, prefix invalidation, and key scanning.
/// All operations are async and accept a <see cref="CancellationToken"/>.
/// </summary>
/// <seealso cref="StangaNetLib.Caching.Storage.InMemoryCacheService"/>
/// <seealso cref="StangaNetLib.Caching.Storage.RedisCacheService"/>
/// <seealso cref="StangaNetLib.Caching.Storage.NullCacheService"/>
public interface ICacheService
{
    /// <summary>
    /// Retrieves a cached value by key.
    /// </summary>
    /// <param name="key">The cache key (without the global <c>KeyPrefix</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The deserialized value, or <c>null</c> if the key does not exist or has expired.</returns>
    Task<Result<T?>> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Stores a value in the cache with an optional expiration.
    /// If <paramref name="expiration"/> is <c>null</c>, the default expiry from
    /// <see cref="CacheSettings.DefaultExpiry"/> is used.
    /// </summary>
    /// <param name="key">The cache key (without the global <c>KeyPrefix</c>).</param>
    /// <param name="value">The value to store. Serialized to JSON internally.</param>
    /// <param name="expiration">How long the entry lives. <c>null</c> uses <see cref="CacheSettings.DefaultExpiry"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result indicating success or failure.</returns>
    Task<Result> SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Removes a specific key from the cache. No-op if the key does not exist.
    /// </summary>
    /// <param name="key">The cache key to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result indicating success or failure.</returns>
    Task<Result> RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether the specified key exists in the cache and has not expired.
    /// </summary>
    /// <param name="key">The cache key to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> if the key exists and has not expired; otherwise <c>false</c>.</returns>
    Task<Result<bool>> ExistsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cache-aside pattern shorthand: returns the cached value if present, otherwise
    /// calls <paramref name="factory"/> to produce the value, stores it in the cache, and returns it.
    /// Thread-safe: concurrent calls with the same key are serialized — the factory is invoked at most once.
    /// </summary>
    /// <param name="key">Cache key.</param>
    /// <param name="factory">Async factory invoked only on a cache miss.</param>
    /// <param name="expiration">Expiration for the newly cached entry. <c>null</c> uses the default expiry.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cached or freshly produced value. Never <c>null</c>.</returns>
    Task<Result<T>> GetOrSetAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Removes all keys that start with <paramref name="keyPrefix"/>.
    /// Useful for invalidating all entries related to a specific entity or context.
    /// Use <see cref="StangaNetLib.Caching.Storage.Keys.CacheKeyBuilder.BuildPrefix"/> to build a safe prefix with a trailing colon.
    /// </summary>
    /// <remarks>
    /// On Redis, this uses SCAN and may not be instantaneous on large keyspaces.
    /// In Redis Cluster mode, SCAN runs only per primary node — keys on other hash slots may not be removed.
    /// </remarks>
    /// <param name="keyPrefix">The key prefix to match. Should end with <c>:</c> to avoid partial matches on adjacent keys.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result indicating success or failure.</returns>
    Task<Result> RemoveByPrefixAsync(string keyPrefix, CancellationToken cancellationToken = default);

    /// <summary>
    /// Scans keys matching <paramref name="pattern"/> using glob syntax (<c>*</c> = any sequence, <c>?</c> = one character).
    /// Returned keys do not include the global <see cref="CacheSettings.KeyPrefix"/>.
    /// </summary>
    /// <remarks>
    /// On Redis, this uses SCAN and is non-blocking but may span multiple round-trips.
    /// In Redis Cluster mode, only keys on the scanned primary node are returned.
    /// </remarks>
    /// <param name="pattern">Glob pattern to match against cache keys.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async sequence of matching keys, without the global prefix.</returns>
    IAsyncEnumerable<string> ScanKeysAsync(string pattern, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves multiple cached values in a single batch operation.
    /// Missing or expired keys map to <c>null</c> in the returned dictionary.
    /// On Redis, uses <c>MGET</c> (one round-trip regardless of key count).
    /// </summary>
    /// <param name="keys">The cache keys to retrieve (without the global <c>KeyPrefix</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A dictionary keyed by the original (un-prefixed) cache key.
    /// Keys with no cached value map to <c>null</c>.
    /// </returns>
    Task<Result<IReadOnlyDictionary<string, T?>>> GetManyAsync<T>(
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Stores multiple values in the cache in a single batch operation.
    /// On Redis, pipelines all <c>SET … EX</c> commands in one batch flush (one round-trip).
    /// </summary>
    /// <param name="entries">The key-value pairs to store (keys without the global <c>KeyPrefix</c>).</param>
    /// <param name="expiration">Expiration applied to every entry. <c>null</c> uses <see cref="CacheSettings.DefaultExpiry"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result indicating success or failure.</returns>
    Task<Result> SetManyAsync<T>(
        IEnumerable<KeyValuePair<string, T>> entries,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Removes multiple keys from the cache in a single batch operation.
    /// On Redis, uses the multi-key <c>DEL</c> overload (one round-trip).
    /// Keys that do not exist are silently ignored.
    /// </summary>
    /// <param name="keys">The cache keys to remove (without the global <c>KeyPrefix</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result indicating success or failure.</returns>
    Task<Result> RemoveManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default);
}
