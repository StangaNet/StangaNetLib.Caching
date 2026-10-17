using FluentAssertions;
using StangaNetLib.Caching.Storage;

namespace StangaNetLib.Caching.Tests.Storage;

public class NullCacheServiceTests
{
    private readonly NullCacheService _sut = new();

    [Fact]
    public async Task GetAsync_AlwaysReturnsNull()
    {
        var result = await _sut.GetAsync<object>("any-key", TestContext.Current.CancellationToken);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task ExistsAsync_AlwaysReturnsFalse()
    {
        var ct = TestContext.Current.CancellationToken;
        await _sut.SetAsync("key", new object(), cancellationToken: ct);
        var exists = await _sut.ExistsAsync("key", ct);
        exists.Value.Should().BeFalse();
    }

    [Fact]
    public async Task GetOrSetAsync_AlwaysCallsFactory()
    {
        var ct = TestContext.Current.CancellationToken;
        var callCount = 0;
        await _sut.GetOrSetAsync("key", ct => { callCount++; return Task.FromResult(new object()); }, cancellationToken: ct);
        await _sut.GetOrSetAsync("key", ct => { callCount++; return Task.FromResult(new object()); }, cancellationToken: ct);
        callCount.Should().Be(2);
    }

    [Fact]
    public async Task RemoveAsync_DoesNotThrow()
    {
        var act = async () => await _sut.RemoveAsync("any", TestContext.Current.CancellationToken);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RemoveByPrefixAsync_DoesNotThrow()
    {
        var act = async () => await _sut.RemoveByPrefixAsync("prefix:", TestContext.Current.CancellationToken);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ScanKeysAsync_AlwaysReturnsEmpty()
    {
        var ct = TestContext.Current.CancellationToken;
        var keys = new List<string>();
        await foreach (var key in _sut.ScanKeysAsync("*", ct))
            keys.Add(key);
        keys.Should().BeEmpty();
    }

    [Fact]
    public async Task GetManyAsync_AlwaysReturnsNullForAllKeys()
    {
        var result = await _sut.GetManyAsync<object>(["a", "b"], TestContext.Current.CancellationToken);
        result.IsSuccess.Should().BeTrue();
        result.Value!["a"].Should().BeNull();
        result.Value!["b"].Should().BeNull();
    }

    [Fact]
    public async Task SetManyAsync_DoesNotThrow()
    {
        var entries = new Dictionary<string, object> { ["k"] = new() };
        var act = async () => await _sut.SetManyAsync<object>(entries, cancellationToken: TestContext.Current.CancellationToken);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RemoveManyAsync_DoesNotThrow()
    {
        var act = async () => await _sut.RemoveManyAsync(["a", "b"], TestContext.Current.CancellationToken);
        await act.Should().NotThrowAsync();
    }
}
