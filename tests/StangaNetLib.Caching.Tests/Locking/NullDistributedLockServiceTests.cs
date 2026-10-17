using FluentAssertions;
using StangaNetLib.Caching.Storage;
using StangaNetLib.Caching.Locking;

namespace StangaNetLib.Caching.Tests.Storage;

public class NullDistributedLockServiceTests
{
    private readonly NullDistributedLockService _sut = new();

    [Fact]
    public async Task TryAcquireAsync_WhenResourceFree_ShouldReturnNonNullScope()
    {
        var scopeResult = await _sut.TryAcquireAsync("resource", TimeSpan.FromSeconds(5), null, TestContext.Current.CancellationToken);
        scopeResult.IsSuccess.Should().BeTrue();
        await scopeResult.Value!.DisposeAsync();
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
        var scopeResult = await _sut.TryAcquireAsync("res", TimeSpan.FromSeconds(1), null, TestContext.Current.CancellationToken);
        var scope = scopeResult.Value!;
        var act = async () =>
        {
            await scope.DisposeAsync();
            await scope.DisposeAsync(); // double dispose
        };
        await act.Should().NotThrowAsync();
    }
}
