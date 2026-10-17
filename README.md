# StangaNetLib.Caching

[![.NET CI](https://github.com/StangaNet/StangaNetLib.Caching/actions/workflows/main.yml/badge.svg)](https://github.com/StangaNet/StangaNetLib.Caching/actions/workflows/main.yml)
![NuGet](https://img.shields.io/badge/nuget-1.0.1-blue)
![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%209.0-512BD4)

StangaNetLib.Caching is a unified, provider-agnostic caching and distributed locking abstraction for .NET. It provides a resilient, thread-safe, and domain-centric interface for managing cached data and synchronization primitives across single-node and multi-node environments.

## Design Philosophy

- **Pluggable Providers**: Seamlessly switch between `InMemory` and `Redis` implementations via configuration without changing your application logic.
- **Resilient Redis Operations**: Automatic integration with `StangaNetLib.Resilience` to wrap Redis commands in a resilience pipeline, ensuring stability under transient network issues.
- **Thundering-Herd Protection**: Built-in synchronization in `GetOrSetAsync` using `StangaNetLib.Concurrency` to prevent multiple concurrent requests from populating the same cache key.
- **Distributed Safety**: Support for distributed locking via `IDistributedLockService` to ensure data consistency in multi-instance deployments.
- **Domain-Centric Key Management**: Structured key building with `CacheKeyBuilder` to maintain organization-wide consistency and prevent key collisions.

## Installation

The package is hosted on **GitHub Packages**.

```xml
<!-- NuGet.config — add the GitHub Packages source -->
<configuration>
  <packageSources>
    <add key="github" value="https://nuget.pkg.github.com/StangaNet/index.json" />
  </packageSources>
</configuration>
```

```xml
<!-- .csproj -->
<PackageReference Include="StangaNetLib.Caching" Version="1.0.1" />
```

---

## Core Components

*The library is organized into functional namespaces. For implementation details, refer directly to the source code.*

| Namespace | Core Components | Purpose |
| :--- | :--- | :--- |
| `StangaNetLib.Caching.Storage` | `ICacheService`, `RedisCacheService`, `InMemoryCacheService`, `CacheKeyBuilder` | Distributed caching, key building, and thundering-herd protection. |
| `StangaNetLib.Caching.Locking` | `IDistributedLockService`, `RedisDistributedLockService`, `InMemoryDistributedLockService` | Distributed locking mechanisms for synchronization. |
| `StangaNetLib.Caching.Configuration` | `CacheSettings` | Centralized configuration for cache and lock providers. |
| `StangaNetLib.Caching.Extensions` | `ServiceCollectionExtensions` | Dependency Injection registration and integration with other StangaNetLib libraries. |

---

## Project Structure

```
StangaNetLib.Caching/
├── src/
│   └── StangaNetLib.Caching/
│       ├── Configuration/    # CacheSettings
│       ├── Extensions/       # ServiceCollectionExtensions, etc.
│       ├── Locking/          # IDistributedLockService, Redis/InMemory implementations
│       └── Storage/          # ICacheService, Redis/InMemory implementations, CacheKeyBuilder
└── tests/
    └── StangaNetLib.Caching.Tests/
```

---

## Quick Start

### Configuration

Define your settings in `appsettings.json`:

```json
{
  "CacheSettings": {
    "Provider": "Redis",
    "RedisConnectionString": "localhost:6379",
    "KeyPrefix": "myapp",
    "DefaultExpiry": "00:30:00"
  }
}
```

### Registration

Register the services in your `Program.cs`:

```csharp
builder.Services.AddStangaNetLibCaching(builder.Configuration);
```

### Usage Example

```csharp
public class MyService(ICacheService cache)
{
    public async Task<Result<string>> GetDataAsync(string userId, CancellationToken ct = default)
    {
        // Use CacheKeyBuilder for structured keys
        string key = CacheKeyBuilder.Build("user_profile", userId);

        // GetOrSetAsync provides thundering-herd protection and cache-aside logic.
        // Returns Result<T> consistent with the StangaNetLib.Core functional error model.
        return await cache.GetOrSetAsync(
            key,
            async token => await FetchFromDbAsync(userId, token),
            TimeSpan.FromMinutes(10),
            ct
        );
    }

    private async Task<string> FetchFromDbAsync(string id, CancellationToken ct)
        => "user_data";
}
```
