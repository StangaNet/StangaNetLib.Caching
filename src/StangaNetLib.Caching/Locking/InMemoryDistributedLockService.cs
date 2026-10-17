using StangaNetLib.Concurrency.Locking;
using StangaNetLib.Core.Common;

namespace StangaNetLib.Caching.Locking;

/// <summary>
/// In-process distributed lock backed by <see cref="IKeyedLock"/> from StangaNetLib.Concurrency.
/// Non-reentrant. Suitable for single-node deployments and tests.
/// For multi-node deployments, use <see cref="RedisDistributedLockService"/> instead.
/// Semaphore entries are managed by the underlying <see cref="IKeyedLock"/> implementation,
/// which uses reference counting to prevent unbounded growth for high-cardinality resource names.
/// </summary>
public sealed class InMemoryDistributedLockService : IDistributedLockService
{
    private readonly IKeyedLock _keyedLock;

    /// <summary>
    /// Initializes a new instance of <see cref="InMemoryDistributedLockService"/>.
    /// </summary>
    /// <param name="keyedLockFactory">
    /// Factory used to create an isolated per-key lock pool for this service.
    /// Each service instance gets its own lock namespace to avoid cross-service key collisions.
    /// </param>
    public InMemoryDistributedLockService(IKeyedLockFactory keyedLockFactory)
        => _keyedLock = keyedLockFactory.Create();

    /// <inheritdoc/>
    /// <remarks>
    /// The <paramref name="expiry"/> parameter is accepted for interface compatibility but is ignored:
    /// in-process semaphores have no TTL and are released when the process terminates or the scope is disposed.
    /// When <paramref name="waitTimeout"/> is <c>null</c> or <see cref="TimeSpan.Zero"/>, the call is non-blocking
    /// (equivalent to <c>TryAcquire</c>). A positive timeout waits up to that duration for the lock.
    /// </remarks>
    public async Task<Result<IDistributedLockScope>> TryAcquireAsync(
        string resource,
        TimeSpan expiry,
        TimeSpan? waitTimeout = null,
        CancellationToken cancellationToken = default)
    {
        var timeout = waitTimeout ?? TimeSpan.Zero;
        var result = await _keyedLock.TryAcquireAsync(resource, timeout, cancellationToken);
        if (result.IsSuccess)
            return Result<IDistributedLockScope>.Success(new LockScope(result.Value!));

        return Result<IDistributedLockScope>.Failure(Error.Conflict("Lock", "Resource is already locked"));
    }

    private sealed class LockScope(IAsyncDisposable handle) : IDistributedLockScope
    {
        public ValueTask DisposeAsync() => handle.DisposeAsync();
    }
}
