# StangaNetLib.Caching — Copilot instructions

- Unified `ICacheService` / `IDistributedLockService` over InMemory and Redis, built on `StangaNetLib.Concurrency` and `StangaNetLib.Resilience`.
- Prefer `Result` / `Result<T>` for cache/lock outcomes; reserve exceptions for invalid configuration / arguments.
- Keep public APIs documented (`///`), provider-agnostic at the call site, and free of host-specific infrastructure.
- DI entry point is `AddStangaNetLibCaching` in `StangaNetLib.Caching.Extensions`.
- Match existing naming, file-scoped namespaces, and EditorConfig / Roslyn conventions.
- Tests use xUnit v3 + FluentAssertions; cover `net8.0`, `net9.0`, and `net10.0` when changing shared behavior. Redis-specific tests may stay integration-only.
