using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using StangaNetLib.Caching.Storage;
using StangaNetLib.Caching.Configuration;
using StangaNetLib.Caching.Tests.Helpers;

namespace StangaNetLib.Caching.Tests.Storage;

public class InMemoryCacheServiceTests : IDisposable
{
    private readonly IMemoryCache _memoryCache;
    private readonly InMemoryCacheService _sut;

    public InMemoryCacheServiceTests()
    {
        _memoryCache = new MemoryCache(new MemoryCacheOptions());
        var settings = new CacheSettings { DefaultExpiry = TimeSpan.FromMinutes(5) };
        _sut = new InMemoryCacheService(_memoryCache, settings, NullLogger<InMemoryCacheService>.Instance, new FakeKeyedLockFactory());
    }

    // --- GetAsync / SetAsync ---

    [Fact]
    public async Task SetAsync_ThenGetAsync_ShouldReturnStoredValue()
    {
        var ct = TestContext.Current.CancellationToken;
        await _sut.SetAsync("key1", new SampleData("Alice", 30), cancellationToken: ct);
        var result = await _sut.GetAsync<SampleData>("key1", ct);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Name.Should().Be("Alice");
        result.Value.Age.Should().Be(30);
    }

    [Fact]
    public async Task GetAsync_WhenKeyDoesNotExist_ShouldReturnNull()
    {
        var result = await _sut.GetAsync<SampleData>("nonexistent", TestContext.Current.CancellationToken);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task SetAsync_ShouldOverwriteExistingKey()
    {
        var ct = TestContext.Current.CancellationToken;
        await _sut.SetAsync("key1", new SampleData("Alice", 30), cancellationToken: ct);
        await _sut.SetAsync("key1", new SampleData("Bob", 25), cancellationToken: ct);
        var result = await _sut.GetAsync<SampleData>("key1", ct);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Name.Should().Be("Bob");
    }

    // --- RemoveAsync ---

    [Fact]
    public async Task RemoveAsync_ShouldDeleteExistingKey()
    {
        var ct = TestContext.Current.CancellationToken;
        await _sut.SetAsync("key1", new SampleData("Alice", 30), cancellationToken: ct);
        await _sut.RemoveAsync("key1", ct);
        var result = await _sut.GetAsync<SampleData>("key1", ct);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task RemoveAsync_NonExistentKey_ShouldNotThrow()
    {
        var act = async () => await _sut.RemoveAsync("does-not-exist", TestContext.Current.CancellationToken);
        await act.Should().NotThrowAsync();
    }

    // --- ExistsAsync ---

    [Fact]
    public async Task ExistsAsync_WhenKeyExists_ShouldReturnTrue()
    {
        var ct = TestContext.Current.CancellationToken;
        await _sut.SetAsync("key1", new SampleData("Alice", 30), cancellationToken: ct);
        var exists = await _sut.ExistsAsync("key1", ct);
        exists.Value.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_WhenKeyDoesNotExist_ShouldReturnFalse()
    {
        var exists = await _sut.ExistsAsync("missing", TestContext.Current.CancellationToken);
        exists.Value.Should().BeFalse();
    }

    // --- GetOrSetAsync ---

    [Fact]
    public async Task GetOrSetAsync_OnCacheMiss_ShouldCallFactoryAndStore()
    {
        var ct = TestContext.Current.CancellationToken;
        var factoryCalled = 0;

        var result = await _sut.GetOrSetAsync(
            "key1",
            async ct => { factoryCalled++; await Task.Yield(); return new SampleData("Factory", 99); },
            cancellationToken: ct);

        factoryCalled.Should().Be(1);
        result.Value!.Name.Should().Be("Factory");

        // Second call: factory should NOT be called again
        await _sut.GetOrSetAsync("key1", ct => { factoryCalled++; return Task.FromResult(new SampleData("X", 0)); }, cancellationToken: ct);
        factoryCalled.Should().Be(1);
    }

    [Fact]
    public async Task GetOrSetAsync_OnCacheHit_ShouldReturnCachedValueWithoutCallingFactory()
    {
        var ct = TestContext.Current.CancellationToken;
        await _sut.SetAsync("key1", new SampleData("Cached", 10), cancellationToken: ct);

        var factoryCalled = false;
        var result = await _sut.GetOrSetAsync("key1", ct => { factoryCalled = true; return Task.FromResult(new SampleData("X", 0)); }, cancellationToken: ct);

        factoryCalled.Should().BeFalse();
        result.Value!.Name.Should().Be("Cached");
    }

    [Fact]
    public async Task GetOrSetAsync_ConcurrentCalls_ShouldCallFactoryOnlyOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        var factoryCallCount = 0;

        var tasks = Enumerable.Range(0, 10).Select(_ =>
            _sut.GetOrSetAsync(
                "concurrent-key",
                async ct =>
                {
                    Interlocked.Increment(ref factoryCallCount);
                    await Task.Delay(20, ct);
                    return new SampleData("Concurrent", 0);
                },
                cancellationToken: ct));

        var results = await Task.WhenAll(tasks);

        factoryCallCount.Should().Be(1);
        results.Should().AllSatisfy(r => r.Value!.Name.Should().Be("Concurrent"));
    }

    // --- RemoveByPrefixAsync ---

    [Fact]
    public async Task RemoveByPrefixAsync_ShouldRemoveAllMatchingKeys_AndLeaveOthers()
    {
        var ct = TestContext.Current.CancellationToken;
        await _sut.SetAsync("profile:user:1", new SampleData("A", 1), cancellationToken: ct);
        await _sut.SetAsync("profile:user:2", new SampleData("B", 2), cancellationToken: ct);
        await _sut.SetAsync("discovery:user:1", new SampleData("C", 3), cancellationToken: ct);

        await _sut.RemoveByPrefixAsync("profile:user:", ct);

        (await _sut.GetAsync<SampleData>("profile:user:1", ct)).Value.Should().BeNull();
        (await _sut.GetAsync<SampleData>("profile:user:2", ct)).Value.Should().BeNull();
        (await _sut.GetAsync<SampleData>("discovery:user:1", ct)).Value.Should().NotBeNull();
    }

    [Fact]
    public async Task RemoveByPrefixAsync_WhenNoKeysMatch_ShouldNotThrow()
    {
        var act = async () => await _sut.RemoveByPrefixAsync("nonexistent:", TestContext.Current.CancellationToken);
        await act.Should().NotThrowAsync();
    }

    // --- ScanKeysAsync ---

    [Fact]
    public async Task ScanKeysAsync_WithWildcardPattern_ShouldReturnMatchingKeys()
    {
        var ct = TestContext.Current.CancellationToken;
        await _sut.SetAsync("profile:user:1", new SampleData("A", 1), cancellationToken: ct);
        await _sut.SetAsync("profile:user:2", new SampleData("B", 2), cancellationToken: ct);
        await _sut.SetAsync("discovery:user:1", new SampleData("C", 3), cancellationToken: ct);

        var keys = new List<string>();
        await foreach (var key in _sut.ScanKeysAsync("profile:*", ct))
            keys.Add(key);

        keys.Should().HaveCount(2);
        keys.Should().Contain("profile:user:1");
        keys.Should().Contain("profile:user:2");
        keys.Should().NotContain("discovery:user:1");
    }

    [Fact]
    public async Task ScanKeysAsync_WithExactPattern_ShouldReturnOnlyExactMatch()
    {
        var ct = TestContext.Current.CancellationToken;
        await _sut.SetAsync("user:1", new SampleData("A", 1), cancellationToken: ct);
        await _sut.SetAsync("user:10", new SampleData("B", 2), cancellationToken: ct);

        var keys = new List<string>();
        await foreach (var key in _sut.ScanKeysAsync("user:1", ct))
            keys.Add(key);

        keys.Should().ContainSingle().Which.Should().Be("user:1");
    }

    [Fact]
    public async Task ScanKeysAsync_WithNoMatch_ShouldReturnEmpty()
    {
        var ct = TestContext.Current.CancellationToken;
        await _sut.SetAsync("profile:user:1", new SampleData("A", 1), cancellationToken: ct);

        var keys = new List<string>();
        await foreach (var key in _sut.ScanKeysAsync("order:*", ct))
            keys.Add(key);

        keys.Should().BeEmpty();
    }

    [Fact]
    public async Task ScanKeysAsync_WithKeyPrefix_ShouldReturnKeysWithPrefixStripped()
    {
        var ct = TestContext.Current.CancellationToken;
        var settings = new CacheSettings { KeyPrefix = "myapp", DefaultExpiry = TimeSpan.FromMinutes(1) };
        var prefixed = new InMemoryCacheService(_memoryCache, settings, NullLogger<InMemoryCacheService>.Instance, new FakeKeyedLockFactory());

        await prefixed.SetAsync("user:1", new SampleData("A", 1), cancellationToken: ct);
        await prefixed.SetAsync("user:2", new SampleData("B", 2), cancellationToken: ct);

        var keys = new List<string>();
        await foreach (var key in prefixed.ScanKeysAsync("user:*", ct))
            keys.Add(key);

        keys.Should().HaveCount(2);
        keys.Should().Contain("user:1");
        keys.Should().Contain("user:2");
        keys.Should().NotContain(k => k.StartsWith("myapp:"));
    }

    // --- KeyPrefix ---

    [Fact]
    public async Task KeyPrefix_ShouldBeAppliedTransparently()
    {
        var ct = TestContext.Current.CancellationToken;
        var settings = new CacheSettings { KeyPrefix = "test", DefaultExpiry = TimeSpan.FromMinutes(1) };
        var prefixed = new InMemoryCacheService(_memoryCache, settings, NullLogger<InMemoryCacheService>.Instance, new FakeKeyedLockFactory());

        await prefixed.SetAsync("mykey", new SampleData("X", 0), cancellationToken: ct);

        var raw = _memoryCache.Get<string>("mykey");
        raw.Should().BeNull();

        var result = await prefixed.GetAsync<SampleData>("mykey", ct);
        result.IsSuccess.Should().BeTrue();
        result.Value!.Name.Should().Be("X");
    }

    // --- SerializerOptions ---

    [Fact]
    public async Task SetAsync_WithCustomSerializerOptions_ShouldRoundtrip()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        };
        var settings = new CacheSettings { DefaultExpiry = TimeSpan.FromMinutes(1), SerializerOptions = options };
        var sut = new InMemoryCacheService(_memoryCache, settings, NullLogger<InMemoryCacheService>.Instance, new FakeKeyedLockFactory());

        await sut.SetAsync("key", new SampleData("Test", 42), cancellationToken: ct);
        var result = await sut.GetAsync<SampleData>("key", ct);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Name.Should().Be("Test");
        result.Value.Age.Should().Be(42);
    }

    // --- Expiry ---

    [Fact]
    public async Task SetAsync_WithShortExpiry_ShouldExpireEntry()
    {
        var ct = TestContext.Current.CancellationToken;
        await _sut.SetAsync("expiring", new SampleData("Short", 1), TimeSpan.FromMilliseconds(50), ct);
        await Task.Delay(150, ct);
        var result = await _sut.GetAsync<SampleData>("expiring", ct);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    // --- GetManyAsync ---

    [Fact]
    public async Task GetManyAsync_ShouldReturnValuesForExistingKeysAndNullForMissing()
    {
        var ct = TestContext.Current.CancellationToken;
        await _sut.SetAsync("a", new SampleData("Alice", 1), cancellationToken: ct);
        await _sut.SetAsync("b", new SampleData("Bob", 2), cancellationToken: ct);

        var result = await _sut.GetManyAsync<SampleData>(["a", "b", "c"], ct);

        result.IsSuccess.Should().BeTrue();
        result.Value!["a"]!.Name.Should().Be("Alice");
        result.Value!["b"]!.Name.Should().Be("Bob");
        result.Value!["c"].Should().BeNull();
    }

    [Fact]
    public async Task GetManyAsync_EmptyKeys_ShouldReturnEmptyDictionary()
    {
        var result = await _sut.GetManyAsync<SampleData>([], TestContext.Current.CancellationToken);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    // --- SetManyAsync ---

    [Fact]
    public async Task SetManyAsync_ShouldStoreAllEntries()
    {
        var ct = TestContext.Current.CancellationToken;
        var entries = new Dictionary<string, SampleData>
        {
            ["x"] = new("X", 10),
            ["y"] = new("Y", 20)
        };

        await _sut.SetManyAsync<SampleData>(entries, cancellationToken: ct);

        (await _sut.GetAsync<SampleData>("x", ct)).Value!.Name.Should().Be("X");
        (await _sut.GetAsync<SampleData>("y", ct)).Value!.Name.Should().Be("Y");
    }

    // --- RemoveManyAsync ---

    [Fact]
    public async Task RemoveManyAsync_ShouldRemoveAllSpecifiedKeys()
    {
        var ct = TestContext.Current.CancellationToken;
        await _sut.SetAsync("k1", new SampleData("A", 1), cancellationToken: ct);
        await _sut.SetAsync("k2", new SampleData("B", 2), cancellationToken: ct);
        await _sut.SetAsync("k3", new SampleData("C", 3), cancellationToken: ct);

        await _sut.RemoveManyAsync(["k1", "k2"], ct);

        (await _sut.GetAsync<SampleData>("k1", ct)).Value.Should().BeNull();
        (await _sut.GetAsync<SampleData>("k2", ct)).Value.Should().BeNull();
        (await _sut.GetAsync<SampleData>("k3", ct)).Value.Should().NotBeNull();
    }

    public void Dispose() => _memoryCache.Dispose();
}

file record SampleData(string Name, int Age);
