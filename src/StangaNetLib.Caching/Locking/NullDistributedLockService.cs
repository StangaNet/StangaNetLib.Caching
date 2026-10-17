using StangaNetLib.Caching.Locking;
using StangaNetLib.Core.Common;

namespace StangaNetLib.Caching.Locking;

/// <summary>
/// A no-op distributed lock implementation that always acquires successfully with zero contention.
/// Use in unit tests to isolate the system under test from lock behavior:
/// <c>TryAcquireAsync</c> always returns a valid scope, disposal is a no-op.
/// </summary>
/// <example>
/// <code>
/// // In test setup:
/// var lockService = new NullDistributedLockService();
/// var useCase = new MyUseCase(cache, lockService, ...);
/// </code>
/// </example>
public sealed class NullDistributedLockService : IDistributedLockService
{
    /// <inheritdoc/>
    /// <remarks>
    /// Always returns a valid, no-op lock scope — never returns <c>null</c>.
    /// The <paramref name="expiry"/> and <paramref name="resource"/> parameters are accepted but ignored.
    /// </remarks>
    public Task<Result<IDistributedLockScope>> TryAcquireAsync(
        string resource,
        TimeSpan expiry,
        TimeSpan? waitTimeout = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Result<IDistributedLockScope>.Success(NullLockScope.Instance));

    private sealed class NullLockScope : IDistributedLockScope
    {
        public static readonly NullLockScope Instance = new();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
