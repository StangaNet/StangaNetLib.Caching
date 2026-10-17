using System.Text;

namespace StangaNetLib.Caching.Storage.Keys;

/// <summary>
/// Builds structured, colon-separated cache keys.
/// Centralizes key construction to avoid scattered string interpolation across use cases.
/// </summary>
/// <example>
/// <code>
/// // Instead of: $"profile:user:{userId}"
/// CacheKeyBuilder.Build("profile", "user", userId.ToString());
///
/// // With prefix applied externally by the service:
/// // "socfit:profile:user:550e8400..."
/// </code>
/// </example>
public static class CacheKeyBuilder
{
    private const char Separator = ':';

    /// <summary>
    /// Builds a cache key by joining the provided parts with <c>:</c>.
    /// Example: <c>Build("product", id)</c> → <c>"product:&lt;id&gt;"</c>
    /// </summary>
    /// <param name="parts">One or more non-empty key segments to join.</param>
    /// <returns>A colon-separated cache key string.</returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="parts"/> is empty or contains a null/empty element.</exception>
    public static string Build(params string[] parts)
    {
        if (parts is null || parts.Length == 0)
            throw new ArgumentException("At least one part is required.", nameof(parts));

        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part))
                throw new ArgumentException("Cache key parts must not be null or empty.", nameof(parts));
        }

        return string.Join(Separator, parts);
    }

    /// <summary>
    /// Builds a cache key using a namespace and a qualifier, with optional additional parts.
    /// Example: <c>Build("discovery", "cursor", userId, page, filter)</c> → <c>"discovery:cursor:&lt;userId&gt;:&lt;page&gt;:&lt;filter&gt;"</c>
    /// </summary>
    /// <param name="ns">The top-level namespace segment (e.g. <c>"discovery"</c>).</param>
    /// <param name="qualifier">The second-level qualifier segment (e.g. <c>"cursor"</c>).</param>
    /// <param name="additionalParts">Zero or more additional non-empty segments appended after the qualifier.</param>
    /// <returns>A colon-separated cache key string.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown if <paramref name="ns"/> or <paramref name="qualifier"/> is null/empty,
    /// or if any element of <paramref name="additionalParts"/> is null/empty.
    /// </exception>
    public static string Build(string ns, string qualifier, params string[] additionalParts)
    {
        if (string.IsNullOrEmpty(ns))
            throw new ArgumentException("Namespace must not be null or empty.", nameof(ns));
        if (string.IsNullOrEmpty(qualifier))
            throw new ArgumentException("Qualifier must not be null or empty.", nameof(qualifier));

        var sb = new StringBuilder(ns).Append(Separator).Append(qualifier);
        foreach (var part in additionalParts)
        {
            if (string.IsNullOrEmpty(part))
                throw new ArgumentException("Cache key parts must not be null or empty.", nameof(additionalParts));
            sb.Append(Separator).Append(part);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Builds a cache key from a dynamic collection of parts by joining them with <c>:</c>.
    /// Useful when key segments come from a runtime collection and <c>ToArray()</c> is inconvenient.
    /// Example: <c>Build(new[] { "product", id })</c> → <c>"product:&lt;id&gt;"</c>
    /// </summary>
    /// <param name="parts">One or more non-empty key segments to join.</param>
    /// <returns>A colon-separated cache key string.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="parts"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">Thrown if <paramref name="parts"/> is empty or contains a null/empty element.</exception>
    public static string Build(IEnumerable<string> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        return Build(parts.ToArray());
    }

    /// <summary>
    /// Builds a prefix key to be used with <see cref="StangaNetLib.Caching.Storage.ICacheService.RemoveByPrefixAsync"/>.
    /// The trailing colon prevents partial matches on keys that share the same prefix string but belong to a different namespace.
    /// Example: <c>BuildPrefix("discovery", userId)</c> → <c>"discovery:&lt;userId&gt;:"</c>
    /// </summary>
    /// <param name="parts">One or more non-empty key segments to join.</param>
    /// <returns>A colon-separated prefix string with a trailing <c>:</c>.</returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="parts"/> is empty or contains a null/empty element.</exception>
    public static string BuildPrefix(params string[] parts)
        => Build(parts) + Separator;
}
