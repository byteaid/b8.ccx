# Forbidden — Test-specific code paths in production code (moved)

The full ban (environment-forked DI, `#if INTEGRATION_TEST`, `Testing:DisableAuth`, fake clients gated by env, `DbInitializer` loading fake data, `UseSeeding` with fake users) lives in **`dotnet-testing`** § forbidden-patterns § 2.

Load `dotnet-testing` and read `forbidden-patterns.md`.

## Companion conventions

The replacement primitives this ban depends on stay in `dotnet-conventions`:

- [../csharp-style/time-provider.md](../csharp-style/time-provider.md) — inject `TimeProvider` instead of forking on env to swap a fake clock.
- [../csharp-style/guid-createversion7.md](../csharp-style/guid-createversion7.md) — `Guid.CreateVersion7()` for deterministic-enough IDs without bypass code.
- [no-aspire-client-integrations.md](no-aspire-client-integrations.md) — read connection strings via `builder.Configuration.GetConnectionString("name")` (no env fork on the registration shape).
- [no-persistent-aspire-resources.md](no-persistent-aspire-resources.md) — ephemeral resources mean tests never need to "reset" the app from the inside.
