using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using StangaNetLib.Caching.Extensions;
using StangaNetLib.Caching.Storage;
using StangaNetLib.Caching.Configuration;
using StangaNetLib.Caching.Tests.Helpers;

namespace StangaNetLib.Caching.Tests.Extensions;

public class CacheServiceExtensionsTests : IDisposable
{
    private readonly IMemoryCache _memoryCache = new MemoryCache(new MemoryCacheOptions());
    private readonly InMemoryCacheService _cache;

    public CacheServiceExtensionsTests()
    {
        var settings = new CacheSettings { DefaultExpiry = TimeSpan.FromMinutes(5) };
        _cache = new InMemoryCacheService(_memoryCache, settings, NullLogger<InMemoryCacheService>.Instance, new FakeKeyedLockFactory());
    }

    // --- GetOrSetIfNotNullAsync ---

    [Fact]
    public async Task GetOrSetIfNotNullAsync_OnMissWithNonNullFactory_ShouldCacheAndReturn()
    {
        var ct = TestContext.Current.CancellationToken;
        var callCount = 0;

        var result = await _cache.GetOrSetIfNotNullAsync(
            "key1",
            async ct => { callCount++; await Task.Yield(); return new Item("A"); },
            cancellationToken: ct);

        result.Should().NotBeNull();
        result!.Name.Should().Be("A");
        callCount.Should().Be(1);

        // Second call: should hit cache, factory not called again
        await _cache.GetOrSetIfNotNullAsync("key1", ct => { callCount++; return Task.FromResult<Item?>(null); }, cancellationToken: ct);
        callCount.Should().Be(1);
    }

    [Fact]
    public async Task GetOrSetIfNotNullAsync_OnMissWithNullFactory_ShouldNotCacheAndReturnNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var callCount = 0;

        var result = await _cache.GetOrSetIfNotNullAsync<Item>(
            "key-null",
            async ct => { callCount++; await Task.Yield(); return null; },
            cancellationToken: ct);

        result.Should().BeNull();

        // Should NOT be cached — factory called again on next invocation
        await _cache.GetOrSetIfNotNullAsync<Item>("key-null", ct => { callCount++; return Task.FromResult<Item?>(null); }, cancellationToken: ct);
        callCount.Should().Be(2);
    }

    // --- GetOrSetAsync with ns/qualifier key building ---

    [Fact]
    public async Task GetOrSetAsync_WithNsAndQualifier_ShouldBuildKeyAndCache()
    {
        var ct = TestContext.Current.CancellationToken;
        var factoryCalled = 0;

        var result = await _cache.GetOrSetAsync(
            ns: "product",
            qualifier: "detail",
            additionalParts: ["42"],
            factory: async ct => { factoryCalled++; await Task.Yield(); return new Item("Widget"); },
            cancellationToken: ct);

        result.Value!.Name.Should().Be("Widget");
        factoryCalled.Should().Be(1);

        // Same key built again — should hit cache
        await _cache.GetOrSetAsync("product", "detail", ["42"], ct => { factoryCalled++; return Task.FromResult(new Item("Other")); }, cancellationToken: ct);
        factoryCalled.Should().Be(1);
    }

    public void Dispose() => _memoryCache.Dispose();
}

file record Item(string Name);
