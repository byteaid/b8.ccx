# MSTest Integration Mechanics

How a test class mounts an Aspire-orchestrated topology, asserts through real surfaces, and consolidates artefacts. Companion to [layout.md](layout.md), which owns the project shape and naming; this file owns the fixture lifecycle.

## Centralized fixture — the canonical shape

The mount/dispose lifecycle lives in **exactly one place**: `AppHostFixture.cs` in the test project root. Every system-exercising `[TestClass]` inherits it. `[ClassInitialize(InheritanceBehavior.BeforeEachDerivedClass)]` re-runs the mount for **each derived class**, so every class still gets its own fresh `DistributedApplication` — centralization is about the code, not about sharing a host. **No second fixture, no class building its own host inline, no `[AssemblyInitialize]` hosting the system-under-test.**

```csharp
// AppHostFixture.cs — the ONE fixture base. The only file in the project
// allowed to call DistributedApplicationTestingBuilder.
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Blaztrap.Aspire.FileLogging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Acme.Inventory.Test;

public abstract class AppHostFixture
{
    protected static DistributedApplication App { get; private set; } = null!;
    protected static string LogsDir { get; private set; } = string.Empty;

    [ClassInitialize(InheritanceBehavior.BeforeEachDerivedClass)]
    public static async Task FixtureInitAsync(TestContext context)
    {
        var className = context.FullyQualifiedTestClassName!.Split('.')[^1];
        LogsDir = Path.Combine(TestArtifacts.RunDir(context), "logs", className);

        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Acme_Inventory_AppHost>(TestSettings.AppHostArgs());

        appHost.Services.ConfigureHttpClientDefaults(c =>
            c.AddStandardResilienceHandler());

        appHost.AddFileLogging(LogsDir);   // per-resource files + apphost.log

        App = await appHost.BuildAsync();
        await App.StartAsync();
    }

    [ClassCleanup(InheritanceBehavior.BeforeEachDerivedClass)]
    public static async Task FixtureCleanupAsync()
    {
        if (App is not null) await App.DisposeAsync();
        App = null!;
    }
}
```

A test class inherits the fixture and adds only its own surface plumbing in a plain `[ClassInitialize]` (MSTest runs the base fixture init first, the derived init second; cleanups run in reverse):

```csharp
using System.Net;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Acme.Inventory.Test.HTTP;

[TestClass]
public class Orders_Tests : AppHostFixture
{
    private static HttpClient _api = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext context)
    {
        await App.ResourceNotifications
            .WaitForResourceHealthyAsync("api")
            .WaitAsync(TestSettings.ResourceHealthyTimeout);

        _api = App.CreateHttpClient("api");
        _api.Timeout = TimeSpan.FromSeconds(5);
    }

    [ClassCleanup]
    public static void ClassCleanup() => _api?.Dispose();

    [TestMethod]
    public async Task CreateOrder_WithValidData_ReturnsCreated()
    {
        var response = await _api.PostAsJsonAsync("/orders", new { sku = "SKU-1", qty = 2 });
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
    }
}
```

Key invariants:

- **`AppHostFixture.cs` is the only file that calls `DistributedApplicationTestingBuilder`.** Greppable — see [layout.md](layout.md) § Enforcement.
- Both fixture methods MUST be `public static` and the init MUST take `TestContext`. The compiler does not enforce this; if `App` is null in a test, that signature is the first thing to check.
- Per-class logs land under `{RunDir}/logs/{ClassName}/` so sequential classes never overwrite each other's files.
- The connection between the test and the system under test is the same surface a real client would use: `HttpClient` from `App.CreateHttpClient(name)`, gRPC client pointed at `App.GetEndpoint(name)`, message bus via the same SDK as production, the CLI as a child process, etc.
- With `Workers = 1` (see § Parallelism) classes run sequentially, so the base's static `App` slot is never contended.

## One suite, one topology — emulators ARE real infra

There is **no "real infra" test tier**. No `[TestCategory("RealInfra")]`, no gated CI job, no filtered subsets, no per-class AppHost args, no behaviour forks. The golden rule — *the code under test is the code that ships* — applies to the whole topology: emulators and stub projects are real infrastructure (real sockets, real protocols, real SDK clients). With zero environment variables set, the complete suite runs on any machine with zero secrets.

**Running the SAME suite against provisioned (non-emulated) infrastructure IS permitted**, under exactly one condition: neither application code nor test code changes in function of it. Same classes, same methods, same assertions, full suite, no filter. The only thing that may differ is the wiring handed to `DistributedApplicationTestingBuilder.CreateAsync(...)` at mount — assembled in ONE consolidated file from environment variables (see § `TestSettings` below). The AppHost reads those args and resolves each resource to the emulator or the provisioned service (`dotnet-aspire` § emulators-and-real-infra); consumer and test code are byte-identical either way.

Consequences for test authoring:

- **A test that reaches a service the AppHost does not orchestrate is a defect.** Real ARM / Graph / mail channels / partner APIs hardwired into the suite mean the AppHost is missing an emulator or a stub project — fix the topology, never tag the test.
- **No native emulator → stub project** (`dotnet-testing` § forbidden-patterns § 1): a real ASP.NET Core resource in the AppHost, reached over real HTTP/gRPC.
- **SDK pins the vendor's hostname → wire at the DNS level.** Some SDKs hardcode endpoints (no base-URL override). The answer is still topology, not code: resolve the pinned hostname to the stub via the container network's DNS / hosts-entry injection on the resource, with the stub serving the vendor's TLS contract (dev cert trust). The consumer keeps zero test-awareness — it genuinely believes it is talking to the vendor.
- **The default run needs no secrets.** A test that fails without a credential when no `TESTRUN_*` variable is set means a resource escaped the AppHost. In a real-infrastructure run, secrets enter exclusively as environment variables consumed by `TestSettings` and forwarded as `CreateAsync` args — never committed, never read anywhere else.

## `TestSettings` — the ONE consolidated run-wiring file

Every run-behaviour knob of the suite lives in a single `TestSettings.cs` at the test project root, each knob backed by an environment variable with a default that works on any machine with zero setup. **It is the only file in the test project allowed to call `Environment.GetEnvironmentVariable`** (greppable — see [layout.md](layout.md) § Enforcement).

```csharp
// TestSettings.cs — single source for topology wiring and run behaviour.
namespace Acme.Inventory.Test;

internal static class TestSettings
{
    // ── Topology ────────────────────────────────────────────────────────────
    /// <summary>false (default) = emulators + stubs; true = provisioned infrastructure.</summary>
    public static bool UseRealInfrastructure { get; } = Bool("TESTRUN_REAL_INFRA");

    /// <summary>Args handed to DistributedApplicationTestingBuilder.CreateAsync.
    /// The ONLY place a topology decision exists in the test project.</summary>
    public static string[] AppHostArgs() => UseRealInfrastructure
        ?
        [
            "UseRealInfrastructure=true",
            $"ConnectionStrings:cosmos={Required("TESTRUN_CS_COSMOS")}",
            $"ConnectionStrings:sb={Required("TESTRUN_CS_SB")}",
        ]
        : [];

    // ── Run behaviour (observability / pace — never assertions) ────────────
    /// <summary>Headed browser for local debugging. CI default: headless.</summary>
    public static bool Headed { get; } = Bool("TESTRUN_HEADED");

    /// <summary>Playwright slow-mo in ms, for watching a flow at human speed.</summary>
    public static int SlowMoMs { get; } = Int("TESTRUN_SLOWMO_MS", 0);

    /// <summary>Resource health-check ceiling (rule: ≥ 3 min for emulators).</summary>
    public static TimeSpan ResourceHealthyTimeout { get; } =
        TimeSpan.FromMinutes(Int("TESTRUN_HEALTHY_TIMEOUT_MIN", 3));

    private static bool Bool(string name) =>
        Environment.GetEnvironmentVariable(name) is "1" or "true" or "True";
    private static int Int(string name, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var v) ? v : fallback;
    private static string Required(string name) =>
        Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"{name} is required when TESTRUN_REAL_INFRA=1.");
}
```

The fixture consumes it at mount — `CreateAsync<Projects.Acme_Inventory_AppHost>(TestSettings.AppHostArgs())` — and Playwright setup consumes `Headed` / `SlowMoMs`. Hard limits on what a knob may do:

- **Knobs change wiring, observability, and pace — NEVER behaviour under assertion.** No `if (TestSettings.UseRealInfrastructure)` around an assertion, a seed, a skip, or a branch in any test method — that is a code fork and a defect.
- **No artefact-path knobs.** Output locations come from `TestContext` only (§ Test artefacts); `TestSettings` must not contain a logs/results directory override.
- **No knob sprawl.** A knob that only one test reads is a smell — knobs are suite-wide by definition.

The `RunAsEmulator()` calls and the `UseRealInfrastructure` / `AsExisting` / `AddConnectionString` switching are owned by the AppHost — see `dotnet-aspire` § emulators-and-real-infra for the producer side. On the test side, the flag enters exclusively through `TestSettings.AppHostArgs()`; no test class, fixture branch, or production file ever references it.

## File logging

`Blaztrap.Aspire.FileLogging` writes one log file per resource PLUS `apphost.log` (AppHost / DCP / Aspire / dashboard categories) under a chosen directory. Call `AddFileLogging` AFTER every `AddProject`/`AddExecutable` and BEFORE `BuildAsync()` — the fixture does this once, in `FixtureInitAsync`:

```csharp
appHost.AddFileLogging(LogsDir);
App = await appHost.BuildAsync();
```

It works identically in `aspire run` and inside `DistributedApplicationTestingBuilder`. The `dotnet-aspire` skill § file-logging owns the integration's plumbing; this skill only consumes it.

**The legacy `Blaztrap.Aspire.Testing.FileLogging` package and its `AddResourceFileLogging(...)` are banned.** It captures per-resource stdout only and drops everything the AppHost emits — which is exactly where startup failures (image missing, port collision, bad connection string) land. A suite wired through the legacy package debugs blind. Migrating is a package swap plus renaming the call to `AddFileLogging`.

## Test artefacts — `TestResults/{run-id}/...` and `TestResults/.auth/`

Two paths matter:

| Kind | Path | Lifetime | Resolved from |
|---|---|---|---|
| **Per-run transients** (logs, traces, screenshots, video, trx, coverage) | `TestResults/{run-id}/...` | One run | `TestContext.TestRunResultsDirectory` |
| **Shared auth state** (`{role}-state.json`) | `TestResults/.auth/` | Across runs | `Path.GetDirectoryName(TestContext.TestRunDirectory)` + `/.auth/` |

There is **no `BLAZTRAP_TEST_RUN_DIR` env var, no `*_LOG_DIR` env var, and no in-repo artefact folder** (`tests/automated/`, a repo-root `.browser-session.json`, anything git-adjacent) — MSTest's `TestContext` is the single source of truth and `TestResults/` is the single destination. Dispersed artefacts are how a suite ends up "manually captured": every consumer (CI upload, post-mortem, the orchestrator's verify step) must know one root, not N conventions. Orchestrators that need a deterministic run folder pass `--results-directory` to `dotnet test` and let MSTest place its run subfolder there.

Canonical helper — put it in `Company.Product.Test/TestArtifacts.cs`, reuse from every fixture:

```csharp
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Acme.Inventory.Test;

internal static class TestArtifacts
{
    /// <summary>Per-run transient root. New folder per `dotnet test` invocation.</summary>
    public static string RunDir(TestContext context) =>
        context.TestRunResultsDirectory
        ?? Path.Combine(AppContext.BaseDirectory, "TestResults");

    /// <summary>Shared auth-state root. Sibling of every per-run folder.</summary>
    public static string AuthDir(TestContext context)
    {
        var runDir = context.TestRunDirectory;
        if (runDir is null)
            return Path.Combine(AppContext.BaseDirectory, "TestResults", ".auth");
        var dir = Path.GetDirectoryName(runDir)!; // .../TestResults
        var path = Path.Combine(dir, ".auth");
        Directory.CreateDirectory(path);
        return path;
    }
}
```

Layout once a run produces artefacts:

```
TestResults/
  .auth/
    user-state.json          ← reused across runs
    admin-state.json
  Deploy_user_20260524-153012/
    In/MACHINE/              ← TestRunResultsDirectory
      logs/
        Orders_Tests/        ← one folder per test class (fixture mounts per class)
          apphost.log
          api.log
          worker.log
          partner-stub.log
        Checkout_Tests/
          ...
      playwright/
        trace-<TestName>.zip
        <TestName>.png
      recorded/              ← stub-project capture files (webhook payloads, …)
      coverage/coverage.cobertura.xml
    TestResults/             ← TestRunDirectory
      Acme.Inventory.Test.trx
```

Wiring trx + coverage to the same root (no env var — pass `--results-directory` explicitly):

```powershell
dotnet test `
  --logger "trx;LogFileName=Acme.Inventory.Test.trx" `
  --results-directory ./TestResults `
  --collect:"XPlat Code Coverage" `
  -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=cobertura
```

Orchestrators may add a date stamp to the results dir for bookkeeping (`./TestResults/2026-05-24-153012/`), but the path inside the test code never changes — `TestArtifacts.RunDir(context)` always resolves correctly.

## Parallelism

MSTest parallelises **classes** by default. With one AppHost per class that is hostile:

- Two classes booting AppHosts at the same time fight over the same emulator ports.
- The dashboard, Docker socket, and DCP do not love simultaneous `StartAsync` calls.
- The fixture base's static `App` slot would be contended.

Pin method-level parallelism inside a single worker in `AssemblyInfo.cs`:

```csharp
[assembly: Parallelize(Workers = 1, Scope = ExecutionScope.MethodLevel)]
```

`Scope = ExecutionScope.MethodLevel` means methods within one class run sequentially against that class's AppHost; `Workers = 1` means classes themselves do not run in parallel. Classes still execute one at a time, each booting and disposing its own host through the fixture.

If a future suite is large enough to warrant cross-class parallelism, the answer is **shard the test run across processes** (multiple `dotnet test` invocations targeting different `--filter`s), not turn back on class-level parallelism inside one process.

## CI

Default job — the whole suite, every PR, zero env vars, zero secrets:

```powershell
dotnet test --results-directory ./TestResults
```

Optional real-infrastructure run — the **same whole suite, no filter**; only the wiring changes, via the `TESTRUN_*` variables `TestSettings` consumes (secrets from federated identity / the pipeline secret store):

```powershell
$env:TESTRUN_REAL_INFRA = "1"
$env:TESTRUN_CS_COSMOS  = "<from secret store>"
$env:TESTRUN_CS_SB      = "<from secret store>"
dotnet test --results-directory ./TestResults
```

There is no filtered subset and no test-code difference between the two runs — see § "One suite, one topology".

## MSTest pitfalls

- **`[ClassInitialize]` signature.** Must be `public static async Task X(TestContext context)`. The compiler is silent if the signature drifts; the method just stops being called.
- **`InheritanceBehavior.BeforeEachDerivedClass` belongs ONLY on `AppHostFixture`.** A derived class adds a plain `[ClassInitialize]` / `[ClassCleanup]` for its own surface plumbing — never a second `BeforeEachDerivedClass` pair, never its own host mount.
- **Ordering.** Base fixture init runs before the derived class's `[ClassInitialize]`; cleanups run in reverse. Code in the derived init can rely on `App` being started.
- **Class-level parallelism** (the MSTest default) breaks AppHost suites — see § Parallelism.
- **Health-check timeout.** `WaitForResourceHealthyAsync` defaults to 30 s. Always pass `TimeSpan.FromMinutes(3)` or longer for cloud / emulator resources.
- **Sharing one AppHost across classes.** Don't — not via `[AssemblyInitialize]`, not via static helpers. Each class mounts and disposes its own through the fixture; only the fixture *code* is shared.

## Topology checklist for a new test class

- [ ] Every dependency the class exercises is orchestrated by the AppHost (emulator, container, or stub project) — nothing escapes to a non-orchestrated external service.
- [ ] The class passes with zero env vars set (default emulator/stub run, no credentials).
- [ ] No `Environment.GetEnvironmentVariable` outside `TestSettings.cs`; no `TestSettings` knob influences an assertion, seed, or branch.
- [ ] SDKs that pin vendor hostnames are wired to the stub at the DNS level (see § "One suite, one topology").
- [ ] `WaitForResourceHealthyAsync` uses `TestSettings.ResourceHealthyTimeout` (≥ 3 minutes for emulators).
- [ ] No `[TestCategory]` tiers — if the class seems to need one, the topology is wrong, not the taxonomy.

## Cross-references

- [layout.md](layout.md) — project shape, surface folders, `.csproj`.
- [seeding.md](seeding.md) — what runs after `WaitForResourceHealthyAsync` to load test data.
- [forbidden-patterns.md](forbidden-patterns.md) — what may NOT appear in `Program.cs` / consumer DI / the test project.
- Sibling skill: `dotnet-aspire` — `DistributedApplicationTestingBuilder.CreateAsync`, registration verbs, `AddFileLogging` plumbing.
- Live (testing overview): https://learn.microsoft.com/en-us/dotnet/aspire/testing/manage-app-host
- Live (`DistributedApplicationTestingBuilder.CreateAsync`): https://learn.microsoft.com/en-us/dotnet/api/aspire.hosting.testing.distributedapplicationtestingbuilder.createasync
- Live (`WaitForResourceHealthyAsync`): https://learn.microsoft.com/en-us/dotnet/api/aspire.hosting.applicationmodel.resourcenotificationservice.waitforresourcehealthyasync
- Live (MSTest `Parallelize`): https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-mstest-intro
- Live (MSTest `ClassInitialize`): https://learn.microsoft.com/en-us/dotnet/api/microsoft.visualstudio.testtools.unittesting.classinitializeattribute
