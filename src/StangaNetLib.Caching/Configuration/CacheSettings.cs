using System.Text.Json;
using System.Text.Json.Serialization;

namespace StangaNetLib.Caching.Configuration;

/// <summary>Selects the cache backend to use.</summary>
public enum CacheProvider
{
    /// <summary>In-process cache using IMemoryCache. Suitable for single-node deployments.</summary>
    InMemory,

    /// <summary>Distributed cache using Redis (StackExchange.Redis). Suitable for multi-node deployments.</summary>
    Redis
}

/// <summary>
/// Configuration for StangaNetLib.Caching. Bind from the <c>"CacheSettings"</c> section in <c>appsettings.json</c>.
/// </summary>
public class CacheSettings
{
    /// <summary>The configuration section name used to bind these settings: <c>"CacheSettings"</c>.</summary>
    public const string SectionName = "CacheSettings";

    /// <summary>Cache backend to use. Default: <see cref="CacheProvider.InMemory"/>.</summary>
    public CacheProvider Provider { get; set; } = CacheProvider.InMemory;

    /// <summary>
    /// Redis connection string. Required when <see cref="Provider"/> is <see cref="CacheProvider.Redis"/>.
    /// Example: <c>"localhost:6379"</c> or <c>"myredis.cache.windows.net:6380,ssl=true,password=..."</c>
    /// </summary>
    public string? RedisConnectionString { get; set; }

    /// <summary>
    /// Optional prefix prepended to every cache key (e.g. <c>"myapp"</c>).
    /// Useful to namespace keys when multiple applications share the same Redis instance.
    /// Must not contain <c>:</c> (colon is the key separator) or whitespace (whitespace produces malformed Redis keys).
    /// Example: with <c>KeyPrefix = "socfit"</c>, the key <c>"profile:user:123"</c> becomes <c>"socfit:profile:user:123"</c>.
    /// Default: <c>null</c> (no prefix).
    /// </summary>
    public string? KeyPrefix { get; set; }

    /// <summary>
    /// Default expiry applied when <c>SetAsync</c> is called without an explicit expiration.
    /// Must be a positive value. Default: 30 minutes.
    /// </summary>
    public TimeSpan DefaultExpiry { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Custom JSON serializer options used for all cache serialization/deserialization.
    /// When <c>null</c>, System.Text.Json defaults are used.
    /// Not bindable from <c>appsettings.json</c> — configure programmatically.
    /// </summary>
    [JsonIgnore]
    public JsonSerializerOptions? SerializerOptions { get; set; }

    /// <summary>
    /// Validates the settings and throws <see cref="InvalidOperationException"/> if any value is invalid.
    /// Called automatically by <c>AddStangaNetLibCaching</c> and <c>AddStangaNetLibRedisCache</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <see cref="Provider"/> is <see cref="CacheProvider.Redis"/> and
    /// <see cref="RedisConnectionString"/> is null or whitespace; when <see cref="DefaultExpiry"/>
    /// is not positive; or when <see cref="KeyPrefix"/> contains a colon or whitespace.
    /// </exception>
    public void Validate()
    {
        if (Provider == CacheProvider.Redis && string.IsNullOrWhiteSpace(RedisConnectionString))
            throw new InvalidOperationException(
                "CacheSettings.RedisConnectionString is required when Provider is Redis.");

        if (DefaultExpiry <= TimeSpan.Zero)
            throw new InvalidOperationException(
                $"CacheSettings.DefaultExpiry must be positive. Got: {DefaultExpiry}.");

        if (!string.IsNullOrEmpty(KeyPrefix) && KeyPrefix.Contains(':'))
            throw new InvalidOperationException(
                $"CacheSettings.KeyPrefix must not contain ':' (colon is the key separator). Got: '{KeyPrefix}'.");

        if (!string.IsNullOrEmpty(KeyPrefix) && KeyPrefix.Any(char.IsWhiteSpace))
            throw new InvalidOperationException(
                $"CacheSettings.KeyPrefix must not contain whitespace (it is embedded in Redis keys). Got: '{KeyPrefix}'.");
    }
}
