# Forbidden — Seed endpoints in application code (moved)

The full ban (`/_test/seed`, `/_test/reset`, `MapPost("/api/admin/seed-test-data", …)`, `DbInitializer` hosted services loading fake data, EF Core `UseSeeding` with test-flavoured rows) lives in **`dotnet-testing`** § forbidden-patterns § 3.

Load `dotnet-testing` and read `forbidden-patterns.md`. Seeding is owned by `dotnet-testing` § seeding (the four canonical strategies under the ephemeral-always invariant).

## Companion conventions

- [no-minimal-apis.md](no-minimal-apis.md) — most banned seed endpoints are also minimal APIs.
- [no-test-specific-branches.md](no-test-specific-branches.md) — pointer to the broader ban on test-flavoured branches.
