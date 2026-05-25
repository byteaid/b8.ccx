# Playwright Wiring for Aspire

Aspire-specific Playwright integration: how to point Playwright at endpoints allocated by a per-class `DistributedApplication` and how `BrowserContext` instances coexist with the team's per-class mount. General Playwright reference (locators, network interception, CI matrix, trace viewer) is owned by the `playwright-dotnet` skill — this file links to it instead of duplicating.

This file assumes the per-class mount from `dotnet-testing` § mstest-integration is in place (`_app` built in `[ClassInitialize]`, healthy, disposed in `[ClassCleanup]`). Shared `AppHostFixtureBase` / `AppHostTestBase<TFixture>` patterns are forbidden — see `dotnet-testing` rule #4.

## Stack

| Package | Purpose |
|---|---|
| `Microsoft.Playwright` | Playwright .NET SDK |
| `Microsoft.Playwright.MSTest` | MSTest integration: `PageTest` / `BrowserTest` / `ContextTest` base classes with auto-managed lifecycle |
| `Aspire.Hosting.Testing` | Provides the resolved `web` endpoint via `App.GetEndpoint("web")` |

The Playwright .NET SDK is browser-driver-based — it does not host a server itself. Aspire owns the app lifecycle; Playwright only opens pages against it.

## Project setup (once)

`.csproj` of the integration-test project:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Playwright"        Version="1.*" />
  <PackageReference Include="Microsoft.Playwright.MSTest" Version="1.*" />
</ItemGroup>
```

After the first `dotnet build`, install browsers:

```powershell
# Windows
pwsh bin/Debug/net10.0/playwright.ps1 install
# Linux / macOS
bin/Debug/net10.0/playwright.sh install
# Channel-specific (e.g. only chromium + msedge)
pwsh bin/Debug/net10.0/playwright.ps1 install chromium msedge
```

In CI, run the install command before `dotnet test`.

## `.runsettings` — global Playwright + MSTest config

```xml
<?xml version="1.0" encoding="utf-8"?>
<RunSettings>
  <Playwright>
    <BrowserName>chromium</BrowserName>
    <LaunchOptions>
      <Channel>msedge</Channel>      <!-- chromium engine, msedge channel -->
      <Headless>true</Headless>
      <SlowMo>0</SlowMo>
    </LaunchOptions>
  </Playwright>
  <MSTest>
    <Parallelize>
      <Workers>0</Workers>
      <Scope>ClassLevel</Scope>
    </Parallelize>
  </MSTest>
</RunSettings>
```

Reference from the `.csproj`:

```xml
<PropertyGroup>
  <RunSettingsFilePath>$(MSBuildThisFileDirectory).runsettings</RunSettingsFilePath>
</PropertyGroup>
```

Or pass explicitly: `dotnet test --settings .runsettings`.

## Per-class Aspire UI test — canonical shape

`PageTest` (from `Microsoft.Playwright.MSTest`) provides auto-managed `Browser`, `Context`, `Page` properties. Each test gets a fresh `Page` with isolated context.

The Aspire-specific bit: each `[TestClass]` mounts its own `DistributedApplication` in `[ClassInitialize]`, stores the resolved `web` endpoint in a `private static`, and the override of `ContextOptions()` reads that endpoint as `BaseURL`. Aspire assigns dynamic ports on every mount, so the URL MUST be resolved at runtime — never hardcoded.

```csharp
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Blaztrap.Aspire.FileLogging;
using Microsoft.Playwright;
using Microsoft.Playwright.MSTest;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Contoso.Foo.Test.UI;

[TestClass]
public sealed class CheckoutUi_Tests : PageTest
{
    private static DistributedApplication _app = null!;
    private static string _baseUrl = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext context)
    {
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Contoso_Foo_AppHost>([]);

        appHost.Services.ConfigureHttpClientDefaults(c =>
            c.AddStandardResilienceHandler());

        appHost.AddFileLogging(Path.Combine(TestArtifacts.RunDir(context), "logs"));

        _app = await appHost.BuildAsync();
        await _app.StartAsync();

        await _app.ResourceNotifications
            .WaitForResourceHealthyAsync("web")
            .WaitAsync(TimeSpan.FromMinutes(3));

        _baseUrl = _app.GetEndpoint("web").ToString();
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        if (_app is not null) await _app.DisposeAsync();
    }

    public override BrowserNewContextOptions ContextOptions() => new()
    {
        BaseURL = _baseUrl,
        IgnoreHTTPSErrors = true,           // dev cert
        ViewportSize = new() { Width = 1280, Height = 800 },
        Locale = "en-US",
        TimezoneId = "America/Mexico_City",
    };

    [TestMethod]
    public async Task Checkout_HappyPath_CompletesOrder()
    {
        await Page.GotoAsync("/");

        await Page.GetByRole(AriaRole.Link, new() { Name = "Catalog" }).ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Add to cart" }).First.ClickAsync();
        await Page.GetByRole(AriaRole.Link, new() { Name = "Cart" }).ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Checkout" }).ClickAsync();

        await Page.GetByLabel("Full name").FillAsync("Ada Lovelace");
        await Page.GetByLabel("Address").FillAsync("10 Downing St");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Place order" }).ClickAsync();

        await Expect(Page.GetByTestId("order-confirmation")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("order-confirmation")).ToContainTextAsync("Thank you");
    }
}
```

Key invariants:

- `_app` is `private static` on the class. There is no abstract base, no `AppHostFixtureBase`, no `AppHostTestBase<TFixture>` — those patterns are banned (`dotnet-testing` rule #4).
- `_baseUrl` is captured once after the resource is Healthy, then reused by every `ContextOptions()` call for the lifetime of the class.
- `_app.CreateHttpClient("api")` is still available inside test methods for HTTP-level seeding mixed with UI assertions (POST an order via API, then UI-verify the listing).
- Two `[TestClass]` instances each mount and dispose their own AppHost. State cannot leak between classes by construction.

## Health-gate before driving the browser

Aspire returns from `StartAsync` when the AppHost process is up — not when every resource is Healthy. The `[ClassInitialize]` above already waits, but stating the rule explicitly:

```csharp
await _app.ResourceNotifications
    .WaitForResourceHealthyAsync("web")
    .WaitAsync(TimeSpan.FromMinutes(3));
```

For Blazor WebAssembly specifically, the server can be Healthy while the WASM runtime is still bootstrapping. Add a per-test gate that waits for an explicit signal:

```csharp
[TestInitialize]
public async Task WaitForBlazorBootstrapAsync()
{
    await Page.GotoAsync("/");
    await Expect(Page.Locator("body.blazor-loaded")).ToBeVisibleAsync(
        new() { Timeout = 30_000 });
}
```

The `body.blazor-loaded` class is added by the root component's `OnAfterRenderAsync` via JS interop. Without an explicit signal, navigation races against the WASM cold-start — flaky.

## Anti-flake checklist (Aspire-specific subset)

These rules prevent the Aspire-specific flakes. For the full Playwright anti-flake catalogue (auto-retry locators, `Expect` over `IsVisibleAsync`, `Thread.Sleep` ban, locator preference order), see `playwright-dotnet`.

1. **Resolve `BaseURL` from `_app.GetEndpoint("web")`** — Aspire ports are dynamic. Hardcoding `http://localhost:5000` breaks the moment a second AppHost runs in parallel, or when CI uses `aspire start --isolated`.
2. **Gate on `Healthy`, not `Running`.** `WaitForResourceHealthyAsync` is mandatory in `[ClassInitialize]` before capturing `_baseUrl`. Resources without a registered health check effectively skip the wait — register one (`.WithHttpHealthCheck("/health/live")`).
3. **Wait for the SPA bootstrap signal.** Blazor WASM, React/Vite, Next.js: a Healthy server is not a ready client. Use a CSS class or a `data-app-ready` attribute, not a fixed sleep.
4. **`IgnoreHTTPSErrors = true`** in `ContextOptions()` — Aspire-issued dev certs are self-signed.
5. **Keep tests off the AppHost ports.** If you need a fixed port for an external tool (Playwright Inspector, a debug proxy), use `aspire start --isolated` so port allocations don't collide between two devs.

## Trace + screenshots on failure

```csharp
[TestInitialize]
public async Task StartTraceAsync()
{
    await Context.Tracing.StartAsync(new()
    {
        Screenshots = true,
        Snapshots = true,
        Sources = true,
    });
}

[TestCleanup]
public async Task SaveTraceOnFailureAsync()
{
    if (TestContext.CurrentTestOutcome == UnitTestOutcome.Passed)
    {
        await Context.Tracing.StopAsync();
        return;
    }

    var artifactsDir = Path.Combine(TestArtifacts.RunDir(TestContext), "playwright");
    Directory.CreateDirectory(artifactsDir);

    var tracePath = Path.Combine(artifactsDir, $"trace-{TestContext.TestName}.zip");
    await Context.Tracing.StopAsync(new() { Path = tracePath });
    TestContext.AddResultFile(tracePath);

    var screenshotPath = Path.Combine(artifactsDir, $"{TestContext.TestName}.png");
    await Page.ScreenshotAsync(new() { Path = screenshotPath, FullPage = true });
    TestContext.AddResultFile(screenshotPath);
}
```

The artefact root resolves through `TestArtifacts.RunDir(TestContext)` (= `TestContext.TestRunResultsDirectory` with an `AppContext.BaseDirectory` fallback) — see `dotnet-testing` § Test artefacts. Traces land alongside the per-resource Aspire logs, so a failed run gives one folder with everything.

View a trace: `pwsh bin/Debug/net10.0/playwright.ps1 show-trace trace-XXX.zip`.

## Parallelism

- A class's `_app` can serve many parallel `Page` / `Context` instances against its own `web` resource — Playwright contexts are independently isolated within the class.
- The bottleneck is the app (CPU, DB connections), not Playwright.
- AppHost-level constraints from `dotnet-testing` § mstest-integration § Parallelism still apply: MSTest 3.x parallelises classes by default, which races on per-class `[ClassInitialize]` (each class trying to allocate emulator ports). Keep the assembly-level `[assembly: Parallelize(Workers = 1, Scope = ExecutionScope.MethodLevel)]` so classes execute sequentially.
- For UI tests with destructive DB reset in `[TestInitialize]`, ensure the reset is scoped per tenant ID or accept method-level sequencing inside the class.

## Debugging

```powershell
$env:PWDEBUG=1
dotnet test --filter FullyQualifiedName~CheckoutUiTests
```

`PWDEBUG=1` forces `headless: false`, opens the Playwright Inspector, pauses on the first command. Locator picker available.

## Common Aspire-specific mistakes

| Mistake | Fix |
|---|---|
| `Page.GotoAsync("/")` without `BaseURL` set | Override `ContextOptions().BaseURL` from the class's `_baseUrl` captured after `_app.GetEndpoint("web")`. Otherwise the URL resolves against `about:blank`. |
| Hardcoded `http://localhost:5000` | Always resolve dynamically from `_app.GetEndpoint(...)` in `[ClassInitialize]`. |
| Class mounted but UI flakes immediately | Missing `WaitForResourceHealthyAsync("web")` in `[ClassInitialize]`, or the `web` resource has no registered health check. |
| Two test classes sharing data | Seed inside each class's `[ClassInitialize]` (see [test-seeding.md](test-seeding.md)) — classes do not share `_app` and must each prepare their own data. |
| Trace files land in `bin/Debug/...` instead of the run root | Use `TestArtifacts.RunDir(TestContext)` per § Trace. |

## Cross-references

- General Playwright (locators, `Expect`, network, CI matrix, codegen): `playwright-dotnet`.
- Per-class MSTest mount: `dotnet-testing` § mstest-integration.
- Seeding the stack the UI runs on top of: [test-seeding.md](test-seeding.md).
- Auth state generation (Playwright login once, reuse across classes): `playwright-dotnet` § auth-storage.
- Live (Playwright .NET intro): https://playwright.dev/dotnet/docs/intro
- Live (Playwright + MSTest): https://playwright.dev/dotnet/docs/test-runners
- Live (Aspire testing): https://learn.microsoft.com/en-us/dotnet/aspire/testing/manage-app-host
