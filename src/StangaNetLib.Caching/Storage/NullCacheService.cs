using System.Runtime.CompilerServices;
using StangaNetLib.Caching.Storage;
using StangaNetLib.Core.Common;

namespace StangaNetLib.Caching.Storage;

/// <summary>
/// A no-op cache implementation that never stores anything.
/// Use in unit tests to isolate the system under test from caching behavior,
/// ensuring the factory is always called and no stale data is returned.
/// </summary>
/// <example>
/// <code>
/// // In test setup:
/// var cache = new NullCacheService();
/// var useCase = new GetProfileUseCase(repo, cache, ...);
/// </code>
/// </example>
public sealed class NullCacheService : ICacheService
{
    /// <summary>Always returns null (cache miss).</summary>
    public Task<Result<T?>> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
        => Task.FromResult(Result<T?>.Success(null));

    /// <summary>No-op.</summary>
    public Task<Result> SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default) where T : class
        => Task.FromResult(Result.Success());

    /// <summary>No-op.</summary>
    public Task<Result> RemoveAsync(string key, CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Success());

    /// <summary>Always returns false.</summary>
    public Task<Result<bool>> ExistsAsync(string key, CancellationToken cancellationToken = default)
        => Task.FromResult(Result<bool>.Success(false));

    /// <summary>Always calls the factory (no caching).</summary>
    public async Task<Result<T>> GetOrSetAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default) where T : class
    {
        try
        {
            var value = await factory(cancellationToken);
            return Result<T>.Success(value);
        }
        catch (Exception ex)
        {
            return Result<T>.Failure(Error.Internal(ex.Message));
        }
    }

    /// <summary>No-op.</summary>
    public Task<Result> RemoveByPrefixAsync(string keyPrefix, CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Success());

    /// <summary>Always returns an empty sequence.</summary>
    public async IAsyncEnumerable<string> ScanKeysAsync(
        string pattern,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield break;
    }

    /// <summary>Returns an empty dictionary (all keys are misses).</summary>
    public Task<Result<IReadOnlyDictionary<string, T?>>> GetManyAsync<T>(
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default) where T : class
    {
        var result = keys.ToDictionary(k => k, _ => (T?)null);
        return Task.FromResult(Result<IReadOnlyDictionary<string, T?>>.Success(result));
    }

    /// <summary>No-op.</summary>
    public Task<Result> SetManyAsync<T>(
        IEnumerable<KeyValuePair<string, T>> entries,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default) where T : class
        => Task.FromResult(Result.Success());

    /// <summary>No-op.</summary>
    public Task<Result> RemoveManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Success());
}
