using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using StangaNetLib.Caching.Storage;
using StangaNetLib.Caching.Extensions;
using StangaNetLib.Caching.Configuration;
using StangaNetLib.Caching.Locking;
using StangaNetLib.Concurrency.Extensions;
using StangaNetLib.Resilience.Execution;
using StangaNetLib.Concurrency.Locking;

namespace StangaNetLib.Caching.Extensions;

/// <summary>
/// Extension methods for registering StangaNetLib.Caching services in the DI container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ICacheService"/>, <see cref="IDistributedLockService"/>, and their dependencies
    /// based on the <c>"CacheSettings"</c> configuration section.
    /// Also registers <see cref="IOptions{CacheSettings}"/> for consumers that need dynamic access to settings.
    /// When <see cref="CacheProvider.InMemory"/> is selected, also registers StangaNetLib.Concurrency services
    /// (<c>IKeyedLock</c>, <c>IKeyedLockFactory</c>, and related primitives) used internally for thundering-herd protection.
    /// When <see cref="CacheProvider.Redis"/> is selected and <see cref="IResilientExecutor"/> is already registered
    /// in the container (via <c>AddStangaNetLibResilience</c>), it is injected automatically into
    /// <see cref="RedisCacheService"/> and <see cref="RedisDistributedLockService"/> to wrap all Redis commands
    /// through the <c>"Caching"</c> resilience pipeline.
    /// </summary>
    /// <remarks>
    /// Throws <see cref="InvalidOperationException"/> if the configuration is invalid
    /// (see <see cref="CacheSettings.Validate"/>).
    /// </remarks>
    /// <example>
    /// appsettings.json:
    /// <code>
    /// {
    ///   "CacheSettings": {
    ///     "Provider": "Redis",
    ///     "RedisConnectionString": "localhost:6379",
    ///     "KeyPrefix": "myapp",
    ///     "DefaultExpiry": "00:30:00"
    ///   }
    /// }
    /// </code>
    /// Program.cs:
    /// <code>
    /// builder.Services.AddStangaNetLibCaching(builder.Configuration);
    /// </code>
    /// </example>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configuration">Application configuration used to bind the <c>"CacheSettings"</c> section.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddStangaNetLibCaching(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register IOptions<CacheSettings> for consumers that want dynamic / injectable settings access.
        services.Configure<CacheSettings>(o => configuration.GetSection(CacheSettings.SectionName).Bind(o));

        var settings = configuration
            .GetSection(CacheSettings.SectionName)
            .Get<CacheSettings>() ?? new CacheSettings();

        settings.Validate();
        services.AddSingleton(settings);

        return settings.Provider == CacheProvider.Redis
            ? services.AddRedisCache(settings)
            : services.AddInMemoryCache();
    }

    /// <summary>
    /// Registers <see cref="ICacheService"/> and <see cref="IDistributedLockService"/> with in-memory implementations.
    /// Use for single-node deployments, development, and testing without a configuration file.
    /// Also registers StangaNetLib.Concurrency services (<c>IKeyedLock</c>, <c>IKeyedLockFactory</c>, and related
    /// primitives) used internally for thundering-herd protection in <see cref="ICacheService.GetOrSetAsync{T}"/>.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddStangaNetLibInMemoryCache(this IServiceCollection services)
    {
        var settings = new CacheSettings { Provider = CacheProvider.InMemory };
        services.AddSingleton(settings);
        services.AddSingleton(Options.Create(settings));
        return services.AddInMemoryCache();
    }

    /// <summary>
    /// Registers <see cref="ICacheService"/> and <see cref="IDistributedLockService"/> with Redis implementations.
    /// Use for multi-node production deployments without a configuration file.
    /// If <see cref="IResilientExecutor"/> is already registered in the container, it is injected automatically
    /// into the Redis providers to wrap all commands through the <c>"Caching"</c> resilience pipeline.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="connectionString">Redis connection string (e.g. <c>"localhost:6379"</c>).</param>
    /// <param name="keyPrefix">
    /// Optional global key prefix. Applied transparently to all keys.
    /// Must not contain <c>:</c>. Default: <c>null</c>.
    /// </param>
    /// <param name="defaultExpiry">
    /// Default cache entry expiry when none is specified. Default: 30 minutes.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the resulting settings fail validation.</exception>
    public static IServiceCollection AddStangaNetLibRedisCache(
        this IServiceCollection services,
        string connectionString,
        string? keyPrefix = null,
        TimeSpan? defaultExpiry = null)
    {
        var settings = new CacheSettings
        {
            Provider = CacheProvider.Redis,
            RedisConnectionString = connectionString,
            KeyPrefix = keyPrefix,
            DefaultExpiry = defaultExpiry ?? TimeSpan.FromMinutes(30)
        };

        settings.Validate();
        services.AddSingleton(settings);
        services.AddSingleton(Options.Create(settings));
        return services.AddRedisCache(settings);
    }

    private static IServiceCollection AddInMemoryCache(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddStangaNetLibConcurrency();
        services.AddSingleton<IDistributedLockService, InMemoryDistributedLockService>();
        services.AddSingleton<ICacheService>(sp => new InMemoryCacheService(
            sp.GetRequiredService<IMemoryCache>(),
            sp.GetRequiredService<CacheSettings>(),
            sp.GetRequiredService<ILogger<InMemoryCacheService>>(),
            sp.GetRequiredService<IKeyedLockFactory>()));

        return services;
    }

    private static IServiceCollection AddRedisCache(this IServiceCollection services, CacheSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.RedisConnectionString))
            throw new InvalidOperationException(
                "CacheSettings.RedisConnectionString is required when Provider is Redis.");

        services.AddSingleton<IConnectionMultiplexer>(
            _ => ConnectionMultiplexer.Connect(settings.RedisConnectionString));

        services.AddSingleton<IDistributedLockService>(sp => new RedisDistributedLockService(
            sp.GetRequiredService<IConnectionMultiplexer>(),
            sp.GetRequiredService<CacheSettings>(),
            sp.GetService<IResilientExecutor>()));

        services.AddSingleton<ICacheService>(sp => new RedisCacheService(
            sp.GetRequiredService<IConnectionMultiplexer>(),
            sp.GetRequiredService<CacheSettings>(),
            sp.GetRequiredService<ILogger<RedisCacheService>>(),
            sp.GetRequiredService<IDistributedLockService>(),
            sp.GetService<IResilientExecutor>()));

        return services;
    }
}
