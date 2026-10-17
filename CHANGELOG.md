# Changelog

All notable changes to **StangaNetLib.Caching** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.0.0] - 2026-10-17

### Added
- `ICacheService` with InMemory and Redis providers, `GetOrSetAsync` cache-aside, prefix invalidation.
- `IDistributedLockService` (InMemory / Redis / Null) and `CacheKeyBuilder`.
- DI registration (`AddStangaNetLibCaching`) with `CacheSettings` binding and validation.
- Integration with `StangaNetLib.Concurrency` (thundering-herd) and `StangaNetLib.Resilience` (Redis pipeline).
- Dual targeting `net8.0` / `net9.0`, XML docs, symbols (`snupkg`), package icon, and MSBuild consumer metadata stamp.
- Depends on `StangaNetLib.Concurrency` 1.0.0 and `StangaNetLib.Resilience` 1.0.0; Microsoft.Extensions.* pinned to 8.x; StackExchange.Redis 2.8.x (avoids ME 10 pull from Redis 3.x).
- CI via shared StangaNet reusable workflow (push / PR / manual; publish outside PRs).

[Unreleased]: https://github.com/StangaNet/StangaNetLib.Caching/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/StangaNet/StangaNetLib.Caching/releases/tag/v1.0.0
