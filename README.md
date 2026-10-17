# StangaNetLib.Caching

[![.NET CI](https://github.com/StangaNet/StangaNetLib.Caching/actions/workflows/main.yml/badge.svg)](https://github.com/StangaNet/StangaNetLib.Caching/actions/workflows/main.yml)
![NuGet](https://img.shields.io/badge/nuget-1.0.0-blue)
![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%209.0-512BD4)

Unified, provider-agnostic caching and distributed locking for .NET. InMemory and Redis backends, thundering-herd protection via [`StangaNetLib.Concurrency`](https://github.com/StangaNet/StangaNetLib.Concurrency), resilient Redis commands via [`StangaNetLib.Resilience`](https://github.com/StangaNet/StangaNetLib.Resilience), and `Result` / `Result<T>` from [`StangaNetLib.Core`](https://github.com/StangaNet/StangaNetLib.Core).

## Design Philosophy

- **Pluggable providers**: switch InMemory / Redis via `CacheSettings` without changing call sites.
- **Resilient Redis**: optional `IResilientExecutor` wraps Redis commands through a named pipeline.
- **Thundering-herd protection**: `GetOrSetAsync` uses keyed locks from Concurrency.
- **Domain-centric keys**: `CacheKeyBuilder` for consistent, collision-resistant keys.

## Installation

The package is hosted on **GitHub Packages** (pulls Concurrency / Resilience / Core from the same feed).

```xml
<!-- NuGet.config — source key must match your packageSourceCredentials -->
<configuration>
  <packageSources>
    <add key="StangaNet" value="https://nuget.pkg.github.com/StangaNet/index.json" />
  </packageSources>
</configuration>
```

```xml
<!-- .csproj -->
<PackageReference Include="StangaNetLib.Caching" Version="1.0.1" />
```

See [CHANGELOG.md](./CHANGELOG.md) for release history.

## Core Components

| Namespace | Components | Purpose |
|---|---|---|
| `StangaNetLib.Caching.Storage` | `ICacheService`, InMemory/Redis/Null, `CacheKeyBuilder` | Cache-aside and key building |
| `StangaNetLib.Caching.Locking` | `IDistributedLockService`, InMemory/Redis/Null | Distributed locks |
| `StangaNetLib.Caching.Configuration` | `CacheSettings` | Provider and Redis options |
| `StangaNetLib.Caching.Extensions` | `AddStangaNetLibCaching`, helpers | DI and convenience APIs |

## Project Structure

```
StangaNetLib.Caching/
├── assets/
│   └── package-icon.png
├── build/
│   └── StangaNetLib.Caching.props
├── src/
│   └── StangaNetLib.Caching/
│       ├── Configuration/
│       ├── Extensions/
│       ├── Locking/
│       └── Storage/
└── tests/
    └── StangaNetLib.Caching.Tests/
```

## Quick Start

```json
{
  "CacheSettings": {
    "Provider": "InMemory",
    "KeyPrefix": "myapp",
    "DefaultExpiry": "00:30:00"
  }
}
```

```csharp
builder.Services.AddStangaNetLibCaching(builder.Configuration);
```

```csharp
public class MyService(ICacheService cache)
{
    public Task<Result<string>> GetDataAsync(string userId, CancellationToken ct = default)
    {
        var key = CacheKeyBuilder.Build("user_profile", userId);
        return cache.GetOrSetAsync(
            key,
            token => FetchFromDbAsync(userId, token),
            TimeSpan.FromMinutes(10),
            ct);
    }

    private static Task<string> FetchFromDbAsync(string id, CancellationToken ct)
        => Task.FromResult("user_data");
}
```

## CI/CD

On push/PR to `main` and `workflow_dispatch`, the shared StangaNet workflow:

1. Restores, builds, and tests on .NET 8 and .NET 9
2. Packs the NuGet package (with symbols)
3. Publishes to GitHub Packages outside pull requests
4. Uploads the `.nupkg` as a build artifact
