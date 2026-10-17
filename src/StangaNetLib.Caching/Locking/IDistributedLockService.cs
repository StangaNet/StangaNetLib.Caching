using StangaNetLib.Core.Common;

namespace StangaNetLib.Caching.Locking;

/// <summary>
/// Represents an acquired distributed lock. Dispose to release the lock.
/// Always use in an <c>await using</c> block to guarantee release even on exceptions.
/// </summary>
public interface IDistributedLockScope : IAsyncDisposable { }

/// <summary>
/// Provides exclusive per-resource locking to prevent concurrent execution of critical sections across nodes.
/// </summary>
/// <seealso cref="StangaNetLib.Caching.Locking.InMemoryDistributedLockService"/>
/// <seealso cref="StangaNetLib.Caching.Locking.RedisDistributedLockService"/>
public interface IDistributedLockService
{
    /// <summary>
    /// Tries to acquire an exclusive lock on <paramref name="resource"/> without waiting.
    /// </summary>
    /// <param name="resource">The resource identifier to lock on.</param>
    /// <param name="expiry">
    /// Maximum duration the lock is held if the holder crashes before disposing.
    /// Prevents permanent resource starvation on unexpected process termination.
    /// </param>
    /// <param name="waitTimeout">
    /// Optional timeout to wait for the lock to become available.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A disposable <see cref="IDistributedLockScope"/> on success, or <c>null</c> if the resource is already locked.
    /// </returns>
    Task<Result<IDistributedLockScope>> TryAcquireAsync(
        string resource,
        TimeSpan expiry,
        TimeSpan? waitTimeout = null,
        CancellationToken cancellationToken = default);
}
