# MSTest Integration Mechanics

How a test class mounts an Aspire-orchestrated topology, asserts through real surfaces, and consolidates artefacts. Companion to [layout.md](layout.md), which owns the project shape and naming; this file owns the per-class lifecycle.

## Per-class mount — the canonical shape

Every test class is fully self-contained: it builds its own `DistributedApplication` in `[ClassInitialize]`, exposes whatever clients the tests need as `private static`, and disposes the app in `[ClassCleanup]`. **No shared base, no `[AssemblyInitialize]`, no inherited fixture.**

```csharp
using System.Net;
using System.Net.Http;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Blaztrap.Aspire.FileLogging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Acme.Inventory.Test.HTTP;

[TestClass]
public class Orders_Tests
{
    private static DistributedApplication _app = null!;
    private static HttpClient _api = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext context)
    {
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Acme_Inventory_AppHost>([]);

        appHost.Services.ConfigureHttpClientDefaults(c =>
            c.AddStandardResilienceHandler());

        appHost.AddFileLogging(ResolveLogsDir(context));

        _app = await appHost.BuildAsync();
        await _app.StartAsync();

        await _app.ResourceNotifications
            .WaitForResourceHealthyAsync("api")
            .WaitAsync(TimeSpan.FromMinutes(3));

        _api = _app.CreateHttpClient("api");
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        _api?.Dispose();
        if (_app is not null) await _app.DisposeAsync();
    }

    [TestMethod]
    public async Task CreateOrder_WithValidData_ReturnsCreated()
    {
        var response = await _api.PostAsJsonAsync("/orders", new { sku = "SKU-1", qty = 2 });
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
    }

    private static string ResolveLogsDir(TestContext context) =>
        Path.Combine(TestArtifacts.RunDir(context), "logs");
}
```

Key invariants:

- `[ClassInitialize]` MUST be `static` and take `TestContext`. The compiler does not enforce this; if `_app` is null in a test, that signature is the first thing to check.
- `_app` and any clients are `private static` — the class owns them, not a base.
- The connection between the test and the system under test is the same surface a real client would use: `HttpClient` from `_app.CreateHttpClient(name)`, gRPC client pointed at `_app.GetEndpoint(name)`, message bus via the same SDK as production, etc.
- Two derived classes hitting the **same** AppHost would race on `[ClassInitialize]`. There is no inheritance; each class brings its own.

## Switching emulator vs real infrastructure

The AppHost reads a single binary flag (`UseRealInfrastructure`) plus connection strings from `builder.Configuration`. The test passes them through the `args` array of `CreateAsync`:

```csharp
var args = new[]
{
    "UseRealInfrastructure=true",
    $"ConnectionStrings:cosmos={Environment.GetEnvironmentVariable("COSMOS_CS")}",
    $"ConnectionStrings:sb={Environment.GetEnvironmentVariable("SB_CS")}",
};

var appHost = await DistributedApplicationTestingBuilder
    .CreateAsync<Projects.Acme_Inventory_AppHost>(args);
```

Mark real-infra classes with `[TestCategory("RealInfra")]` so CI can filter them:

```csharp
[TestClass]
[TestCategory("RealInfra")]
public class Orders_RealInfra_Tests { /* same shape, args populated */ }
```

`UseRealInfrastructure`, the `RunAsEmulator()` calls, and `AsExisting`/`AddConnectionString` switching are owned by the AppHost — see the `dotnet-aspire` skill § emulators-and-real-infra for the producer side.

## File logging

`Blaztrap.Aspire.FileLogging` writes one log file per resource plus AppHost / DCP / Aspire categories under a chosen directory. Call `AddFileLogging` AFTER every `AddProject`/`AddExecutable` and BEFORE `BuildAsync()`:

```csharp
appHost.AddFileLogging(ResolveLogsDir(context));
_app = await appHost.BuildAsync();
```

It works identically in `aspire run` and inside `DistributedApplicationTestingBuilder`. The `dotnet-aspire` skill § file-logging owns the integration's plumbing; this skill only consumes it.

## Test artefacts — `TestResults/{run-id}/...` and `TestResults/.auth/`

Two paths matter:

| Kind | Path | Lifetime | Resolved from |
|---|---|---|---|
| **Per-run transients** (logs, traces, screenshots, video, trx, coverage) | `TestResults/{run-id}/...` | One run | `TestContext.TestRunResultsDirectory` |
| **Shared auth state** (`{role}-state.json`) | `TestResults/.auth/` | Across runs | `Path.GetDirectoryName(TestContext.TestRunDirectory)` + `/.auth/` |

There is **no `BLAZTRAP_TEST_RUN_DIR` env var** — MSTest's `TestContext` is the single source of truth. Orchestrators that need a deterministic run folder pass `--results-directory` to `dotnet test` and let MSTest place its run subfolder there.

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
        apphost.log
        api.log
        worker.log
        partner-stub.log
      playwright/
        trace-<TestName>.zip
        <TestName>.png
      screenshots/
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

MSTest 3.x parallelises **classes** by default. With per-class AppHosts that is hostile:

- Two classes booting AppHosts at the same time fight over the same emulator ports.
- The dashboard, Docker socket, and DCP do not love simultaneous `StartAsync` calls.

Pin method-level parallelism inside a single worker in `AssemblyInfo.cs`:

```csharp
[assembly: Parallelize(Workers = 1, Scope = ExecutionScope.MethodLevel)]
```

`Scope = ExecutionScope.MethodLevel` means methods within one class run sequentially against that class's AppHost; `Workers = 1` means classes themselves do not run in parallel. Classes still execute one at a time, each booting and disposing its own host.

If a future suite is large enough to warrant cross-class parallelism, the answer is **shard the test run across processes** (multiple `dotnet test` invocations targeting different `--filter`s), not turn back on class-level parallelism inside one process.

## Filtering in CI

```powershell
# Default job — emulated only. Always runs.
dotnet test --filter "TestCategory!=RealInfra"

# Dedicated real-infra job — only when env vars are present.
dotnet test --filter "TestCategory=RealInfra"
```

The emulated job runs on every PR with no secrets. The real-infra job uses federated identity (OIDC / workload identity), runs nightly or on demand, and reuses the same test code.

## MSTest pitfalls

- **`[ClassInitialize]` signature.** Must be `public static async Task X(TestContext context)`. The compiler is silent if the signature drifts; the method just stops being called.
- **`InheritanceBehavior.BeforeEachDerivedClass` is not used here** because the team's discipline forbids inherited fixtures. If you encounter a project that inherits a `ClassInitialize`, treat it as legacy and migrate to the per-class shape.
- **Class-level parallelism** (default in 3.x) breaks AppHost suites — see § Parallelism.
- **`[AssemblyCleanup]` async support requires MSTest 3.x.** Pin 3.6+.
- **Health-check timeout.** `WaitForResourceHealthyAsync` defaults to 30 s. Always pass `TimeSpan.FromMinutes(3)` or longer for cloud / emulator resources.
- **Sharing `_app` across classes.** Don't. Even via static helpers. Each class mounts and disposes its own.

## CI checklist for a new real-infra test class

- [ ] Class is decorated with `[TestCategory("RealInfra")]`.
- [ ] `BuildArgs` declares **every** env var it reads.
- [ ] CI exports those env vars from a secret store, gated by branch / schedule.
- [ ] Emulated default job filter is in place: `--filter "TestCategory!=RealInfra"`.
- [ ] Real-infra job filter is in place: `--filter "TestCategory=RealInfra"`.
- [ ] `WaitForResourceHealthyAsync` timeouts ≥ 3 minutes for any cloud resource.
- [ ] No real-infra credential is committed; secrets come from federated identity or pipeline secret store.

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
