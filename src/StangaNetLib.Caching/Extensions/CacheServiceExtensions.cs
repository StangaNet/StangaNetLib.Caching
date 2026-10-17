using StangaNetLib.Caching.Storage;
using StangaNetLib.Caching.Storage.Keys;
using StangaNetLib.Core.Common;

namespace StangaNetLib.Caching.Extensions;

/// <summary>
/// Extension methods for <see cref="ICacheService"/> that provide higher-level helpers.
/// </summary>
public static class CacheServiceExtensions
{
    /// <summary>
    /// Cache-aside pattern that handles nullable factory results:
    /// if the factory returns <c>null</c>, the result is <b>not</b> cached and <c>null</c> is returned.
    /// Use this when the data source may legitimately return nothing (e.g. entity not found)
    /// and you do not want to cache the absence.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="ICacheService.GetOrSetAsync{T}"/>, this overload does <b>not</b> provide thundering-herd
    /// protection — concurrent callers on the same key may all reach the factory on a miss.
    /// For protection, prefer <see cref="ICacheService.GetOrSetAsync{T}"/> with a non-null sentinel value.
    /// </remarks>
    /// <param name="cache">The cache service.</param>
    /// <param name="key">Cache key (without the global <c>KeyPrefix</c>).</param>
    /// <param name="factory">Async factory that may return <c>null</c>. Only invoked on a cache miss.</param>
    /// <param name="expiration">Expiration for the newly cached entry. <c>null</c> uses the default expiry.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cached or freshly produced value, or <c>null</c> if the factory returned <c>null</c>.</returns>
    public static async Task<T?> GetOrSetIfNotNullAsync<T>(
        this ICacheService cache,
        string key,
        Func<CancellationToken, Task<T?>> factory,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default) where T : class
    {
        var cached = await cache.GetAsync<T>(key, cancellationToken);
        if (cached.IsSuccess && cached.Value != null)
            return cached.Value;

        var value = await factory(cancellationToken);
        if (value != null)
            await cache.SetAsync(key, value, expiration, cancellationToken);

        return value;
    }

    /// <summary>
    /// Builds a structured cache key from <paramref name="ns"/>, <paramref name="qualifier"/>, and
    /// <paramref name="additionalParts"/> via <see cref="CacheKeyBuilder.Build(string, string, string[])"/>,
    /// then delegates to <see cref="ICacheService.GetOrSetAsync{T}"/>.
    /// Eliminates the repetitive key-building boilerplate in use-case classes.
    /// </summary>
    /// <param name="cache">The cache service.</param>
    /// <param name="ns">Top-level namespace segment (e.g. <c>"product"</c>).</param>
    /// <param name="qualifier">Second-level qualifier segment (e.g. <c>"detail"</c>).</param>
    /// <param name="additionalParts">Zero or more additional key segments.</param>
    /// <param name="factory">Async factory invoked only on a cache miss.</param>
    /// <param name="expiration">Expiration for the newly cached entry. <c>null</c> uses the default expiry.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cached or freshly produced value wrapped in a <see cref="Result{T}"/>.</returns>
    public static Task<Result<T>> GetOrSetAsync<T>(
        this ICacheService cache,
        string ns,
        string qualifier,
        string[] additionalParts,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default) where T : class
    {
        var key = CacheKeyBuilder.Build(ns, qualifier, additionalParts);
        return cache.GetOrSetAsync(key, factory, expiration, cancellationToken);
    }
}
