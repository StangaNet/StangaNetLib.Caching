using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StangaNetLib.Caching.Extensions;
using StangaNetLib.Caching.Locking;
using StangaNetLib.Caching.Storage;
using StangaNetLib.Concurrency.Locking;

namespace StangaNetLib.Caching.Tests.Extensions;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddStangaNetLibCaching_InMemory_RegistersExpectedServices()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CacheSettings:Provider"] = "InMemory",
                ["CacheSettings:KeyPrefix"] = "test",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddStangaNetLibCaching(configuration);

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ICacheService>().Should().BeOfType<InMemoryCacheService>();
        provider.GetRequiredService<IDistributedLockService>().Should().NotBeNull();
        provider.GetRequiredService<IKeyedLock>().Should().NotBeNull();
    }

    [Fact]
    public void AddStangaNetLibInMemoryCache_RegistersCacheService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddStangaNetLibInMemoryCache();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ICacheService>().Should().BeOfType<InMemoryCacheService>();
    }
}

