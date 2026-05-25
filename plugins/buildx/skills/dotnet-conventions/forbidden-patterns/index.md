# .NET conventions — Forbidden patterns

Patterns banned across the team's codebase. Each entry follows the strict 4-block format:

```
What it looks like
  — minimal code sample that matches the pattern

Why it's banned
  — 1–3 lines, team reasoning (incidents, invariants)

What to do instead
  — the canonical replacement, with a link to the framework skill

Enforcement
  — eradicate on sight while in the file (scope-bounded) / report / block PR
```

## Final topics

| Pattern | File |
|---|---|
| Minimal APIs (`app.MapGet/MapPost/…`) | [no-minimal-apis.md](no-minimal-apis.md) |
| Warning suppression (`#pragma warning disable`, `[SuppressMessage]`, `<NoWarn>`) | [no-warning-suppression.md](no-warning-suppression.md) |
| Uppercase characters in route templates | [no-uppercase-routes.md](no-uppercase-routes.md) |
| Test-specific code paths in production code (pointer; full ban in `dotnet-testing`) | [no-test-specific-branches.md](no-test-specific-branches.md) |
| Mocks / fakes wired into consumer DI (pointer; full ban in `dotnet-testing`) | [no-mocks-in-consumer-di.md](no-mocks-in-consumer-di.md) |
| Seed endpoints — `/_test/seed`, `DbInitializer`, `UseSeeding` with fake data (pointer; full ban in `dotnet-testing`) | [no-seed-endpoints.md](no-seed-endpoints.md) |
| Persistent Aspire resources (`WithDataVolume`, `ContainerLifetime.Persistent`) | [no-persistent-aspire-resources.md](no-persistent-aspire-resources.md) |
| Aspire client integration packages (`Aspire.Microsoft.*`, `Aspire.StackExchange.*`) | [no-aspire-client-integrations.md](no-aspire-client-integrations.md) |
| Hardcoded secrets / connection strings / URLs in source | [no-hardcoded-secrets.md](no-hardcoded-secrets.md) |
| `.proto` files outside the dedicated gRPC project | [no-proto-outside-dedicated-project.md](no-proto-outside-dedicated-project.md) |
| `DateTime.UtcNow` / `DateTime.Now` (must use `TimeProvider`) | [no-datetime-utcnow.md](no-datetime-utcnow.md) |
| `Guid.NewGuid()` or non-version-7 Guids for new IDs | [no-non-v7-guids.md](no-non-v7-guids.md) |
| Third-party mappers / mediators — Mapperly, MediatR, Mediator (martinothamar), Mapster, Brighter — banned alongside AutoMapper | [no-automapper-no-mediatr.md](no-automapper-no-mediatr.md) |
| Editing test files outside the testing scope (pointer; full ban in `dotnet-testing`) | [no-touching-test-files.md](no-touching-test-files.md) |
| Touching the AppHost or ServiceDefaults from a non-engineer agent | [no-touching-apphost-from-consumer.md](no-touching-apphost-from-consumer.md) |
| Try/catch that does no real work (and missing global try/catch at the app-layer entry point) | [try-catch-must-do-work.md](try-catch-must-do-work.md) |
| Static / manual bypass of the DI motor (statics, service locator, manual `new` of registered types, `Activator.CreateInstance`) | [no-static-bypass-of-di.md](no-static-bypass-of-di.md) |
| Duplicate / ambiguous models, services, helpers (search-before-create discipline) | [no-duplicate-or-ambiguous-models.md](no-duplicate-or-ambiguous-models.md) |
| Deviation from the official hexagonal architecture (wrong project, concrete-adapter dependency, broken dependency-flow invariant) | [no-architecture-deviation.md](no-architecture-deviation.md) |

## See also

- [../build-quality/clean-as-you-touch.md](../build-quality/clean-as-you-touch.md) — the scope-bounded eradication policy
- `dotnet-aspire`
