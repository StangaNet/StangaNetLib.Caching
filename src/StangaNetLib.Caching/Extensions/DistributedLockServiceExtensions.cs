using StangaNetLib.Caching.Locking;

namespace StangaNetLib.Caching.Extensions;

/// <summary>Extension methods for <see cref="IDistributedLockService"/>.</summary>
public static class DistributedLockServiceExtensions
{
    /// <summary>
    /// Polls until an exclusive lock on <paramref name="resource"/> is acquired or <paramref name="waitTimeout"/> elapses.
    /// Each retry waits a random jitter between 20 ms and 80 ms to reduce thundering herd when multiple callers compete.
    /// </summary>
    /// <param name="lockService">The lock service to acquire from.</param>
    /// <param name="resource">The resource identifier to lock on.</param>
    /// <param name="expiry">Maximum duration the lock is held if the holder crashes before disposing.</param>
    /// <param name="waitTimeout">Maximum time to wait before giving up.</param>
    /// <param name="cancellationToken">Cancellation token. Throws <see cref="OperationCanceledException"/> if cancelled while waiting.</param>
    /// <returns>
    /// A disposable <see cref="IDistributedLockScope"/> if the lock was acquired within <paramref name="waitTimeout"/>;
    /// otherwise <c>null</c>.
    /// </returns>
    public static async Task<IDistributedLockScope?> WaitAndAcquireAsync(
        this IDistributedLockService lockService,
        string resource,
        TimeSpan expiry,
        TimeSpan waitTimeout,
        CancellationToken cancellationToken = default)
    {
        var deadline = DateTimeOffset.UtcNow + waitTimeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var scopeResult = await lockService.TryAcquireAsync(resource, expiry, default, cancellationToken);
            if (scopeResult.IsSuccess)
                return scopeResult.Value;

            var remaining = deadline - DateTimeOffset.UtcNow;
            // Random jitter between 20 ms and 80 ms reduces thundering herd on simultaneous waiters.
            var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(20, 81));
            var delay = remaining < jitter ? remaining : jitter;
            if (delay <= TimeSpan.Zero)
                break;

            await Task.Delay(delay, cancellationToken);
        }

        return null;
    }
}
