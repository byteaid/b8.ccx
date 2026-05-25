---
name: dotnet-conventions
description: Team-wide non-negotiable coding standards and forbidden patterns for .NET 10 / C# 14. No third-party libraries for cross-cutting concerns; first-party only. Bans: minimal APIs, AutoMapper / Mapster / Mapperly, MediatR / Mediator (martinothamar) / Brighter, `DateTime.UtcNow`, `Guid.NewGuid()` for new IDs, warning suppression, uppercase routes, persistent Aspire resources, Aspire client integrations. Positives: sealed by default, readonly record struct, `TimeProvider`, `Guid.CreateVersion7()`, hexagonal `Result`/`ErrorCode`, hand-written `IXxxMapper`, `LoggerMessage` + `JsonSerializerContext`, hexagonal layout, `[Company].[Product][.{Module}]` naming, clean-as-you-touch, zero-warnings. Testing rules (single `Company.Product.Test` project, no third-party mocks, no test branches in production, no seed endpoints, no consumer-DI fakes) are owned by `dotnet-testing`.
when_to_use: |
  - Triggers: team conventions, ban list, forbidden patterns, no third-party libraries for cross-cutting concerns, first-party only, IXxxMapper, hexagonal layout, sealed by default, TimeProvider, Guid.CreateVersion7, no AutoMapper / Mapster / Mapperly / MediatR / Mediator / Brighter, no minimal APIs, no DateTime.UtcNow, no warning suppression, clean-as-you-touch, three-attempts-then-search, zero-warnings, LoggerMessage, JsonSerializerContext.
  - Tasks: review C# against team rules; eradicate banned patterns inside edited files; pick the first-party replacement; lay out a new solution; rename projects to `[Company].[Product][.{Module}]`; enforce zero-warnings before handback; produce a handback report.
allowed-tools: Glob, Grep, Read
user-invocable: false
---

# .NET Team Conventions

L1 dispatcher. Concrete content lives in L2 sub-indexes and L3 leaves. Open the topic that matches the trigger; do not read the whole tree.

## Mental model

A **provider-agnostic, framework-agnostic** rule book. Lives ABOVE the per-technology skills (`dotnet-aspire`, `dotnet-hexagonal-architecture`, `dotnet-testing`, `dotnet-file-based-apps`, `dotnet-system-commandline`) and below `CLAUDE.md`. Two responsibilities:

1. **Positive rules.** What modern C# 14 / .NET 10 code looks like on this team — sealed by default, `readonly record struct`, `IAsyncEnumerable<T>`, `ValueTask`, `TimeProvider`, `Guid.CreateVersion7()`, hexagonal `Result` base + app-wide `ErrorCode` enum, hand-written `IXxxMapper` services, BCL source generators (`LoggerMessage`, `JsonSerializerContext`), hexagonal layout, `[Company].[Product][.{Module}]` naming.
2. **Forbidden patterns.** A pinned list of things that are eradicated on sight inside files you're already editing — minimal APIs, warning suppression, uppercase routes, persistent Aspire resources, Aspire client integration packages, hardcoded secrets, `.proto` outside the dedicated project, `DateTime.UtcNow`, non-v7 GUIDs, third-party mappers (AutoMapper / Mapster / Mapperly), third-party mediators (MediatR / Mediator (martinothamar) / Brighter), touching the AppHost from a non-owner, **try/catch that does no real work and missing global try/catch at the application-layer entry point** ([forbidden-patterns/try-catch-must-do-work.md](forbidden-patterns/try-catch-must-do-work.md)), **static / manual bypass of the DI motor** ([forbidden-patterns/no-static-bypass-of-di.md](forbidden-patterns/no-static-bypass-of-di.md)), **duplicate or ambiguous models / services / helpers — search-before-create discipline** ([forbidden-patterns/no-duplicate-or-ambiguous-models.md](forbidden-patterns/no-duplicate-or-ambiguous-models.md)), **deviation from the official hexagonal architecture** ([forbidden-patterns/no-architecture-deviation.md](forbidden-patterns/no-architecture-deviation.md)). Testing-related bans (third-party mocks, test branches in production, seed endpoints, consumer-DI fakes, editing test files outside the testing scope) live in `dotnet-testing` § forbidden-patterns.

What this skill is **not**:

- Not a tutorial. Reference doc — rule statement, rationale, canonical shape, enforcement.
- Not a per-framework deep dive. Aspire wiring, EF Core query patterns, Blazor componentry, BFF auth — each lives in its own skill. If a rule is *both* forbidden AND framework-specific (e.g., minimal APIs vs ASP.NET Core), the forbidden entry here is the *policy*; the canonical replacement lives in the framework skill.
- Not negotiable per-project. Exceptions go through architecture sign-off; the worker never decides on its own.

## Sub-index

| Topic | When to open | Index |
|---|---|---|
| C# style | `sealed` by default, `readonly record struct`, `TimeProvider`, `Guid.CreateVersion7`, hexagonal `Result`/`ErrorCode`, async hygiene, English-only, dotnet CLI only | [csharp-style/index.md](csharp-style/index.md) |
| Source generators | `LoggerMessage`, `JsonSerializerContext` (BCL only — no third-party generators) | [source-generators/index.md](source-generators/index.md) |
| Project layout | Hexagonal layers, dependency flow, `.slnx` logical groups, `[Company].[Product][.{Module}]` naming, technology-named adapter projects | [project-layout/index.md](project-layout/index.md) |
| Forbidden patterns | Minimal APIs, warning suppression, uppercase routes, persistent Aspire resources, Aspire client integrations, hardcoded secrets, `.proto` outside dedicated project, `DateTime.UtcNow`, non-v7 GUIDs, third-party mappers / mediators, touching the AppHost | [forbidden-patterns/index.md](forbidden-patterns/index.md) |
| Build quality | Zero-warnings rule, clean-as-you-touch, 3-attempts-then-search, handback format (CHANGELOG discipline now lives in `development-documentation` § changelog) | [build-quality/index.md](build-quality/index.md) |
| Code analysis & style enforcement | Roslyn `CA*`/`IDE*` analyzers, `AnalysisLevel`/`AnalysisMode`, EditorConfig severity grammar, naming-rule schema, nullable RT setup, suppression mechanics, trim/AOT analyzers (`IL2xxx`/`IL3xxx`), banned-symbols, public-API tracker, `dotnet format` | [code-analysis.md](code-analysis.md) |

## Hard rules (must survive compaction)

1. **`dotnet build` must exit clean** — zero errors AND zero warnings — before any handback. Suppression of any kind is forbidden; fix the root cause. See [build-quality/zero-warnings-rule.md](build-quality/zero-warnings-rule.md).
2. **Clean-as-you-touch.** Inside any file you edit, eradicate every forbidden pattern from [forbidden-patterns/index.md](forbidden-patterns/index.md) — scope-bounded to that file. See [build-quality/clean-as-you-touch.md](build-quality/clean-as-you-touch.md).
3. **No third-party libraries for cross-cutting concerns.** First-party only. Mapping uses hand-written `IXxxMapper` services; mediators default to no mediator (direct service calls + delegate-first events). See [forbidden-patterns/no-automapper-no-mediatr.md](forbidden-patterns/no-automapper-no-mediatr.md) and `dotnet-hexagonal-architecture` § core-and-infrastructure.
4. **Testing rules live in `dotnet-testing`.** Single `[Company].[Product].Test` project, integration tests only, no third-party mocking libs, no test-specific code paths in production, no seed endpoints, no consumer-DI fakes — load `dotnet-testing` for the full set.
5. **Standard clients only.** `Aspire.Microsoft.*` / `Aspire.StackExchange.*` integrations are banned in consumer hosts; read the connection string via `builder.Configuration.GetConnectionString("name")`. See [forbidden-patterns/no-aspire-client-integrations.md](forbidden-patterns/no-aspire-client-integrations.md).
6. **Three attempts, then web-search.** After three same-symptom failures, STOP and search official sources with the exact error in quotes. See [build-quality/three-attempts-then-search.md](build-quality/three-attempts-then-search.md).
7. **Search before create.** Before introducing a new type, service, mapper, helper, or extension, search the glossary, data-model, and codebase for an existing shape — extend / adjust / specialise that instead. Duplicates and near-duplicates are forbidden. See [forbidden-patterns/no-duplicate-or-ambiguous-models.md](forbidden-patterns/no-duplicate-or-ambiguous-models.md).
8. **No useless try/catch; one global handler at the boundary.** A `catch` block must transform, retry, compensate, or escalate — not just log + re-throw. Every application-layer entry point (command/query handler `Handle` method) carries exactly ONE global try/catch that turns unexpected exceptions into a typed `FailedResult` with `ErrorCode.UnhandledException`. See [forbidden-patterns/try-catch-must-do-work.md](forbidden-patterns/try-catch-must-do-work.md).
9. **DI motor is the only wiring mechanism.** No statics, no service locator inside handlers, no manual `new` of container-known types. Allowed exceptions are pure statics (extensions, source-generated partials, immutable lookups) and a documented `// EXCEPTION-DI-BYPASS:` marker with a named constraint. See [forbidden-patterns/no-static-bypass-of-di.md](forbidden-patterns/no-static-bypass-of-di.md).
10. **Stay in the official architecture.** Hexagonal (ports-and-adapters) is the canon; the dependency-flow invariants in `dotnet-hexagonal-architecture` are absolute. Any deviation introduced by a slice is flagged; project-wide non-conformance is recorded as a `structural` debt row. See [forbidden-patterns/no-architecture-deviation.md](forbidden-patterns/no-architecture-deviation.md).

## See also

- `dotnet-aspire` — AppHost wiring, file logging, emulator vs real.
- `dotnet-hexagonal-architecture` — canonical hexagonal architecture; authoritative on disagreements with this skill's project-layout leaves.
- `dotnet-testing` — single `Company.Product.Test` layout, MSTest mechanics, seeding strategies, testing-related forbidden patterns.
- `dotnet-file-based-apps` — single-`.cs` runnables.
- `dotnet-system-commandline` — CLI surface design.
