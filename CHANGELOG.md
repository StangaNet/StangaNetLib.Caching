# Changelog

All notable changes to **StangaNetLib.Caching** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]


## [1.0.2] - 2026-10-23

### Changed
- Add `net10.0` target framework alongside `net8.0` and `net9.0`.
- Require .NET SDK 10 (global.json) to multi-target `net8.0` / `net9.0` / `net10.0`; opt in Microsoft.Testing.Platform as the `dotnet test` runner.

## [1.0.1] - 2026-10-17

### Fixed
- Republish with Microsoft.Extensions.* 8.x and StackExchange.Redis 2.8.x dependency graph. GitHub Packages retained a prior `1.0.0` nuspec that pulled ME 10.x / Redis 3.x, causing NU1605 downgrades in consumers (e.g. Auth).

## [1.0.0] - 2026-10-17

### Added
- `ICacheService` with InMemory and Redis providers, `GetOrSetAsync` cache-aside, prefix invalidation.
- `IDistributedLockService` (InMemory / Redis / Null) and `CacheKeyBuilder`.
- DI registration (`AddStangaNetLibCaching`) with `CacheSettings` binding and validation.
- Integration with `StangaNetLib.Concurrency` (thundering-herd) and `StangaNetLib.Resilience` (Redis pipeline).
- Dual targeting `net8.0` / `net9.0`, XML docs, symbols (`snupkg`), package icon, and MSBuild consumer metadata stamp.
- Depends on `StangaNetLib.Concurrency` 1.0.0 and `StangaNetLib.Resilience` 1.0.0; Microsoft.Extensions.* pinned to 8.x; StackExchange.Redis 2.8.x (avoids ME 10 pull from Redis 3.x).
- CI via shared StangaNet reusable workflow (push / PR / manual; publish outside PRs).

[Unreleased]: https://github.com/StangaNet/StangaNetLib.Caching/compare/v1.0.2...HEAD
[1.0.2]: https://github.com/StangaNet/StangaNetLib.Caching/compare/v1.0.1...v1.0.2
[1.0.1]: https://github.com/StangaNet/StangaNetLib.Caching/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/StangaNet/StangaNetLib.Caching/releases/tag/v1.0.0
