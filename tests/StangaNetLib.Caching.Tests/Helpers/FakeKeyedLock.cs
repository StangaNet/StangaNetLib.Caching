using System.Collections.Concurrent;
using StangaNetLib.Concurrency;
using StangaNetLib.Concurrency.Locking;
using StangaNetLib.Core.Common;

namespace StangaNetLib.Caching.Tests.Helpers;

/// <summary>
/// Test-only <see cref="IKeyedLockFactory"/> that creates <see cref="FakeKeyedLock"/> instances.
/// </summary>
internal sealed class FakeKeyedLockFactory : IKeyedLockFactory
{
    public IKeyedLock Create() => new FakeKeyedLock();
}

/// <summary>
/// Test-only <see cref="IKeyedLock"/> using a <see cref="ConcurrentDictionary{TKey,TValue}"/> of
/// <see cref="SemaphoreSlim"/> entries. Not reference-counted — entries are not cleaned up on release.
/// Suitable for short-lived test scenarios only.
/// </summary>
internal sealed class FakeKeyedLock : IKeyedLock
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _sems = new(StringComparer.Ordinal);

    public async Task<IAsyncDisposable> AcquireAsync(string key, CancellationToken cancellationToken = default)
    {
        var sem = _sems.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(cancellationToken);
        return new Handle(sem);
    }

    public async Task<Result<IAsyncDisposable>> TryAcquireAsync(string key, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var sem = _sems.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        if (!await sem.WaitAsync(timeout, cancellationToken))
            return Result<IAsyncDisposable>.Failure(Error.Conflict("Lock", "Timeout"));
        return Result<IAsyncDisposable>.Success(new Handle(sem));
    }

    private sealed class Handle(SemaphoreSlim sem) : IAsyncDisposable
    {
        private int _disposed;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                sem.Release();
            return ValueTask.CompletedTask;
        }
    }
}
