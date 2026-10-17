using FluentAssertions;
using StangaNetLib.Caching.Storage;
using StangaNetLib.Caching.Locking;
using StangaNetLib.Caching.Tests.Helpers;

namespace StangaNetLib.Caching.Tests.Storage;

public class InMemoryDistributedLockServiceTests
{
    private readonly InMemoryDistributedLockService _sut = new(new FakeKeyedLockFactory());

    // --- Acquire ---

    [Fact]
    public async Task TryAcquireAsync_WhenResourceFree_ShouldReturnScope()
    {
        var ct = TestContext.Current.CancellationToken;
        var scopeResult = await _sut.TryAcquireAsync("resource-1", TimeSpan.FromSeconds(10), null, ct);

        scopeResult.IsSuccess.Should().BeTrue();
        await scopeResult.Value!.DisposeAsync();
    }

    [Fact]
    public async Task TryAcquireAsync_WhenAlreadyLocked_ShouldReturnNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var first = await _sut.TryAcquireAsync("resource-1", TimeSpan.FromSeconds(10), null, ct);
        var second = await _sut.TryAcquireAsync("resource-1", TimeSpan.FromSeconds(10), null, ct);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeFalse();

        await first.Value!.DisposeAsync();
    }

    [Fact]
    public async Task TryAcquireAsync_DifferentResources_ShouldAcquireIndependently()
    {
        var ct = TestContext.Current.CancellationToken;
        var scope1 = await _sut.TryAcquireAsync("resource-1", TimeSpan.FromSeconds(10), null, ct);
        var scope2 = await _sut.TryAcquireAsync("resource-2", TimeSpan.FromSeconds(10), null, ct);

        scope1.IsSuccess.Should().BeTrue();
        scope2.IsSuccess.Should().BeTrue();

        await scope1.Value!.DisposeAsync();
        await scope2.Value!.DisposeAsync();
    }

    // --- Release ---

    [Fact]
    public async Task DisposeAsync_ShouldReleaseLock_AllowingReacquisition()
    {
        var ct = TestContext.Current.CancellationToken;
        var first = await _sut.TryAcquireAsync("resource-1", TimeSpan.FromSeconds(10), null, ct);
        await first!.Value!.DisposeAsync();

        var second = await _sut.TryAcquireAsync("resource-1", TimeSpan.FromSeconds(10), null, ct);
        second.IsSuccess.Should().BeTrue();
        await second.Value!.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_CalledMultipleTimes_ShouldNotThrow()
    {
        var ct = TestContext.Current.CancellationToken;
        var scopeResult = await _sut.TryAcquireAsync("resource-1", TimeSpan.FromSeconds(10), null, ct);
        var scope = scopeResult.Value!;
        await scope.DisposeAsync();

        var act = async () => await scope.DisposeAsync();
        await act.Should().NotThrowAsync();
    }

    // --- Concurrency ---

    [Fact]
    public async Task TryAcquireAsync_WhileLockHeld_ConcurrentCallersShouldAllFail()
    {
        var ct = TestContext.Current.CancellationToken;
        var holder = await _sut.TryAcquireAsync("shared", TimeSpan.FromSeconds(10), null, ct);

        var results = await Task.WhenAll(
            Enumerable.Range(0, 5).Select(_ => _sut.TryAcquireAsync("shared", TimeSpan.FromSeconds(10), null, ct)));

        results.Should().AllSatisfy(r => r.IsSuccess.Should().BeFalse());

        await holder!.Value!.DisposeAsync();
    }

    [Fact]
    public async Task TryAcquireAsync_AfterRelease_ShouldBeReacquirableByNextCaller()
    {
        var ct = TestContext.Current.CancellationToken;

        var first = await _sut.TryAcquireAsync("resource-1", TimeSpan.FromSeconds(10), null, ct);
        first.IsSuccess.Should().BeTrue();
        await first.Value!.DisposeAsync();

        var second = await _sut.TryAcquireAsync("resource-1", TimeSpan.FromSeconds(10), null, ct);
        second.IsSuccess.Should().BeTrue();
        await second.Value!.DisposeAsync();
    }
}
