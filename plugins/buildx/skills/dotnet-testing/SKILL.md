---
name: dotnet-testing
description: Team-canonical testing reference for .NET 10 / C# 14 — single `[Company].[Product].Test` project (singular `Test`), MSTest only, **real integration tests only** (no unit tests, no second project, no mocks, no in-memory substitutions of critical infrastructure, no test-only adaptations in production code). Authored by `dotnet-test-designer` (never by `dotnet-developer`), bound 1:1 to the flows in `docs/features/FT-*/flows/FL-*-*.md` via the FQN recorded in each flow's `## Test` block. Surface-folder layout (`HTTP/`, `UI/`, `gRPC/`, `Cli/`, `Service/`, `Worker/`, `Queue/`, `Webhook/`), one `[TestClass]` per area in `{Area}_Tests.cs`, method names `{Action}_{Scenario}_{Expectation}`, per-class `DistributedApplication` mount via `[ClassInitialize]` + dispose in `[ClassCleanup]` (no shared `AppHostFixtureBase`; `[AssemblyInitialize]` permitted ONLY for non-AppHost suite-wide setup such as Playwright auth-state generation), MSTest 3.x parallelism settings, `Blaztrap.Aspire.FileLogging` integration, transient artefacts under `TestContext.TestRunResultsDirectory` and shared auth state under `TestResults/.auth/`, four canonical seeding strategies (direct client / container init scripts / emulator+SDK / eventing) under the ephemeral-always invariant, and the test-related forbidden patterns (no third-party mocking libs, no test branches in production code, no seed endpoints, no mocks in consumer DI, no test-specific code paths).
when_to_use: |
  - Trigger keywords: integration test, MSTest, ClassInitialize, AssemblyInitialize, DistributedApplicationTestingBuilder, AppHost test, test seeding, WithInitFiles, AfterResourcesCreatedEvent, ResourceReadyEvent, TestRunResultsDirectory, TestResults/.auth, Company.Product.Test, surface folder, {Area}_Tests.cs, no mocks, no Moq, no NSubstitute, no FakeItEasy, no WireMock, no _test/seed, no DbInitializer, ephemeral-always.
  - Task shapes: scaffold the single `Company.Product.Test` project; place a new test class under the right surface folder; pick a method name; mount the AppHost per class; switch a flaky shared `AppHostFixtureBase` to per-class mount; add or audit test data seeding; eradicate a test-specific branch / seed endpoint / DI mock from production code; route an inbound test-tooling change away from the application code; review a PR for testing rule compliance.
allowed-tools: Bash, Edit, Glob, Grep, Monitor, NotebookEdit, PowerShell, Read, Write
user-invocable: false
paths: ["**/*.Test.csproj", "**/*.Test/**/*.cs", "**/AssemblyInfo.cs", "**/TestData/**"]
---

# .NET Testing — Authoring Reference

L1 dispatcher. Concrete chapters live in L2 sub-files.

## Mental model

Testing on this team is **real integration testing only**, against the **real Aspire-orchestrated topology**, from **one** test project per product. The test exercises the same object graph that ships to production — no in-memory swaps, no DI mocks, no behaviour-forking environment branches. Containers and projects are real. Stubs for third parties (Stripe, Twilio, partner APIs) are real ASP.NET Core projects, registered in the AppHost like any other resource.

Every test class is fully **self-contained**: it builds its own `DistributedApplication` in `[ClassInitialize]` and disposes it in `[ClassCleanup]`. There is no cross-class fixture and no `[AssemblyInitialize]` mounting a shared host. The cost of one AppHost per class is paid intentionally — debuggability and isolation outweigh boot overhead.

## Ownership

Tests are authored by `dotnet-test-designer`, not by `dotnet-developer`. The 1:1 correspondence with flows is non-negotiable: every `FL-NNN-*.md` under `docs/features/FT-*/flows/` has a `## Test` block whose `FQN` field points to exactly one test method in this project. When a flow changes shape or a new flow is added, the test-designer is dispatched to write / extend the test BEFORE the developer touches production code. See agent `dotnet-test-designer`.

## Non-negotiable rules (must survive compaction)

1. **Single test project.** Exactly one `[Company].[Product].Test` (singular `Test`, never plural `.Tests`, never `.UnitTests` / `.IntegrationTests` / `.E2ETests` / `.WebTests` / `.Smoke` / `.Acceptance`). Lives under `test/[Company].[Product]/[Company].[Product].Test/`, never under `src/`. See [layout.md](layout.md).
2. **MSTest only.** xUnit, NUnit, bUnit are not used. Pin `MSTest.TestFramework` and `MSTest.TestAdapter` ≥ 3.6.x.
3. **Real integration tests only — no unit tests, ever.** Every test exercises a real surface: HTTP through the Aspire AppHost, Playwright against the running UI, real gRPC client, real CLI invocation, real queue / event triggers. "Is this integration or end-to-end?" is an unproductive debate; if a behaviour cannot be exercised through a real surface, the flow itself is mis-modelled — escalate to `analyst`.
4. **Per-class AppHost mount.** `[ClassInitialize]` builds the `DistributedApplication`; `[ClassCleanup]` disposes it. **No shared `AppHostFixtureBase`.** State from class A cannot leak into class B by construction. `[AssemblyInitialize]` is permitted ONLY for suite-wide setup that does NOT mount the system-under-test (e.g., Playwright auth-state generation against a short-lived auxiliary AppHost; pre-computing reference data files). It is forbidden as a vehicle to host the production AppHost the tests will exercise. See [mstest-integration.md](mstest-integration.md).
5. **Surface-folder layout.** Folders inside the test project match the surface taxonomy: `HTTP/`, `UI/`, `Grpc/`, `Cli/`, `Service/`, `Worker/`, `Queue/`, `Webhook/`. One `[TestClass]` per area, file name `{Area}_Tests.cs`, method names `{Action}_{Scenario}_{Expectation}`. See [layout.md](layout.md).
6. **No third-party mocking libraries.** `Moq`, `NSubstitute`, `FakeItEasy`, `WireMock.Net` are banned in `[Company].[Product].Test.csproj`. When a third party has no native emulator, ship a real stub project and register it in the AppHost. See [forbidden-patterns.md](forbidden-patterns.md).
7. **No test-specific code paths in production.** No `if (env.IsEnvironment("Testing")) …`, no `#if INTEGRATION_TEST`, no `services.AddSingleton<I…, Fake…>()` guarded by an environment check, no `MapPost("/_test/seed", …)` / `/_test/reset` endpoints, no `DbInitializer` hosted service that loads fake data, no EF Core `UseSeeding` lambda inserting "Demo User". The app behaves identically in dev, tests, and prod. See [forbidden-patterns.md](forbidden-patterns.md).
8. **Ephemeral resources, always.** No `WithDataVolume()`, no `ContainerLifetime.Persistent`, no `WithBindMount` on data folders. Every run starts from a blank container. See [seeding.md](seeding.md) § Invariants.
9. **Seed code lives in the test project**, under `Seeding/`. One file per concern (`SqlSeedData.cs`, `CosmosSeedData.cs`), `internal static class`, `internal static async Task ApplyAsync(...)`, idempotent. Pick the strategy per resource. See [seeding.md](seeding.md).
10. **MSTest 3.x parallelism is hostile to AppHost suites.** Class-level parallelism is the default; two derived classes hitting the same AppHost race on `[ClassInitialize]` and fight for the same emulator ports. Pin `[assembly: Parallelize(Workers = 1, Scope = ExecutionScope.MethodLevel)]` in `AssemblyInfo.cs`. See [mstest-integration.md](mstest-integration.md) § Parallelism.
11. **`WaitForResourceHealthyAsync` timeouts ≥ 3 minutes** for any cloud/emulator resource. The default 30 s frequently flakes on cold-starting Service Bus / Cosmos / SQL emulators.
12. **Transient artefacts use `TestContext.TestRunResultsDirectory`; shared auth state uses `TestResults/.auth/`.** Per-run transients (logs, traces, screenshots, video, trx, coverage) land under `TestContext.TestRunResultsDirectory` (fallback: `Path.Combine(AppContext.BaseDirectory, "TestResults")`). Authentication state files (`{role}-state.json`) are shared across runs and live at `TestResults/.auth/`, computed as `Path.GetDirectoryName(TestContext.TestRunDirectory)` + `/.auth/` (fallback: `Path.Combine(AppContext.BaseDirectory, "TestResults", ".auth")`). No `BLAZTRAP_TEST_RUN_DIR` env var — MSTest's `TestContext` is the single source of truth. Per-resource logs come from `Blaztrap.Aspire.FileLogging` (`appHost.AddFileLogging(logsDir)` after every `AddProject`/`AddExecutable` and before `Build()`). See [mstest-integration.md](mstest-integration.md) § Test artefacts.

## Sub-index

| Topic | File | Load when |
|---|---|---|
| Single-project layout, surface folders, file/class/method naming, `.csproj` shape | [layout.md](layout.md) | Scaffolding the test project, placing a new class, naming a new test method. |
| MSTest integration mechanics: `DistributedApplicationTestingBuilder`, per-class mount, parallelism, file logging, consolidated `TestResults/{run-id}/...` | [mstest-integration.md](mstest-integration.md) | Authoring or auditing a test class; debugging a flaky `[ClassInitialize]`; wiring file logs / trx / coverage. |
| Test data seeding — four canonical strategies + the ephemeral-always invariant + anti-pattern catalogue | [seeding.md](seeding.md) | Adding test data; auditing a `WithDataVolume`; deciding between direct client / `WithInitFiles` / emulator+SDK / eventing. |
| Forbidden patterns — third-party mocks, test branches, seed endpoints, mocks in consumer DI | [forbidden-patterns.md](forbidden-patterns.md) | Eradicating a banned shape inside a file you're already editing; reviewing a PR for testing rule compliance. |
| Debugging and diagnostics — failure-triage checklist, IDE attach, `dotnet-counters` / `-trace` / `-dump` / `-gcdump` / `-stack`, DiagnosticSource subscriptions, common failure shapes | [debugging-and-diagnostics.md](debugging-and-diagnostics.md) | A test fails, hangs, leaks, or flakes; you need to inspect the live AppHost or post-mortem a dump. |

## Quick decision matrix

| Need | Pick |
|---|---|
| Brand-new test project | [layout.md](layout.md) — single `[Company].[Product].Test`, surface folders, MSTest. |
| Add a new test class | One `[TestClass]` per area, file `{Area}_Tests.cs` under the matching surface folder. Per-class `[ClassInitialize]` mount. |
| Pick a test method name | `{Action}_{Scenario}_{Expectation}` — e.g. `CreateOrder_WithValidData_ReturnsCreated`. |
| Seed test data | [seeding.md](seeding.md) — direct client (SQL/Mongo/Redis/Kafka) / `WithInitFiles` (Postgres) / emulator+SDK (Azure) / eventing subscriber (cross-resource or HTTP-driven). |
| Capture per-resource logs | `appHost.AddFileLogging(logsDir)` from `Blaztrap.Aspire.FileLogging`. See [mstest-integration.md](mstest-integration.md). |
| Suite is flaky / class-level races | Pin `[assembly: Parallelize(Workers = 1, Scope = ExecutionScope.MethodLevel)]`. See [mstest-integration.md](mstest-integration.md) § Parallelism. |
| Tempted to mock a dependency | Don't. Ship a stub project and register it in the AppHost. See [forbidden-patterns.md](forbidden-patterns.md). |
| Tempted to add `if (Testing) seed…` in `Program.cs` | Don't. Move to a seeding strategy in the test project. See [forbidden-patterns.md](forbidden-patterns.md) and [seeding.md](seeding.md). |
| Filter the real-infra job in CI | `dotnet test --filter "TestCategory!=RealInfra"` for emulated; `=RealInfra` for the gated job. |

## Cross-references

- Live (Aspire testing overview): https://learn.microsoft.com/en-us/dotnet/aspire/testing/manage-app-host
- Live (`DistributedApplicationTestingBuilder.CreateAsync`): https://learn.microsoft.com/en-us/dotnet/api/aspire.hosting.testing.distributedapplicationtestingbuilder.createasync
- Live (`ResourceNotifications.WaitForResourceHealthyAsync`): https://learn.microsoft.com/en-us/dotnet/api/aspire.hosting.applicationmodel.resourcenotificationservice.waitforresourcehealthyasync
- Live (MSTest `Parallelize`): https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-mstest-intro
- Live (MSTest `ClassInitialize`): https://learn.microsoft.com/en-us/dotnet/api/microsoft.visualstudio.testtools.unittesting.classinitializeattribute
- Live (`WithInitFiles` for Postgres): https://learn.microsoft.com/en-us/dotnet/api/aspire.hosting.postgresbuilderextensions.withinitfiles
- Live (Aspire eventing): https://learn.microsoft.com/en-us/dotnet/aspire/app-host/eventing
- Related skill: `dotnet-aspire` — AppHost wiring, registration verbs, emulator vs real switching, file-logging plumbing primitives.
- Related skill: `dotnet-hexagonal-architecture` — project breakdown, the surface taxonomy that drives the folder names, dependency-flow invariants.
- Related skill: `dotnet-conventions` — zero-warnings, clean-as-you-touch, three-attempts-then-search.
- Related skill: `development-methodology` — the fixed test-first orchestrated cycle (test-designer writes the failing real test → developer implements → developer runs `dotnet test`); no TDD vs direct selection.
- Related skill: `development-documentation` § flow — the `## Test` block where each test's FQN is recorded, bound 1:1 to its flow.
- Sibling agent: `dotnet-test-designer` — the agent that authors every test in this project.
- Sibling agent: `dotnet-developer` — the agent that implements until the tests pass and runs `dotnet test`.
- Repo rules: `AGENTS.md` § Skills.
