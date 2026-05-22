# Playwright Wiring for Aspire

Aspire-specific Playwright integration: how to point Playwright at endpoints allocated by the `DistributedApplication` and how to share that application across `BrowserContext` instances. General Playwright reference (locators, network interception, CI matrix, trace viewer) is owned by the `playwright-dotnet` skill — this file links to it instead of duplicating.

This file assumes the fixture from [integration-testing.md](integration-testing.md) is in place (`AspireTestHost.App` resolved, healthy, accessible).

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

## Aspire-specific base class — `AspirePageTest`

`PageTest` (from `Microsoft.Playwright.MSTest`) provides auto-managed `Browser`, `Context`, `Page` properties. Each test gets a fresh `Page` with isolated context.

The Aspire-specific bit: override `ContextOptions()` to inject the resolved endpoint as `BaseURL`. Aspire assigns dynamic ports on every run, so this MUST be resolved at runtime — never hardcoded.

```csharp
using Microsoft.Playwright;
using Microsoft.Playwright.MSTest;

namespace Contoso.Foo.Test.Ui;

public abstract class AspirePageTest : PageTest
{
    public override BrowserNewContextOptions ContextOptions() => new()
    {
        // Resolve from the shared fixture — see integration-testing.md.
        BaseURL = AppHostTestBase<EmulatedFixture>.Fixture.App
            .GetEndpoint("web").ToString(),
        IgnoreHTTPSErrors = true,           // dev cert
        ViewportSize = new() { Width = 1280, Height = 800 },
        Locale = "en-US",
        TimezoneId = "America/Mexico_City",
    };
}
```

Concrete test class — note it inherits both the Aspire fixture base AND the Playwright base via the abstract class:

```csharp
[TestClass]
public sealed class CheckoutUiTests : AspirePageTest
{
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

## Sharing the `DistributedApplication` with Playwright

The fixture pattern from [integration-testing.md](integration-testing.md) (`AppHostFixtureBase` + `AppHostTestBase<TFixture>`) already produces a single `App` per closed generic. UI tests reuse it the same way HTTP tests do — by resolving `AppHostTestBase<TFixture>.Fixture.App.GetEndpoint("web")` in `ContextOptions()`.

Why this works:
- The browser context is created per test (Playwright's default isolation), but the underlying Aspire process / containers are shared across the whole test class — and across all test classes inheriting the same closed `AppHostTestBase<TFixture>`.
- The endpoint URL is stable for the lifetime of `App` — resolving it once per test (in `ContextOptions()`) is fine.
- `App.CreateHttpClient("api")` is still available for tests that need to mix HTTP-level setup with UI assertions (e.g., POST to seed an order, then UI-verify it).

## Health-gate before driving the browser

Aspire returns from `StartAsync` when the AppHost process is up — not when every resource is Healthy. Always wait for `Healthy` on the `web` resource before any test runs:

```csharp
// In AppHostFixtureBase.InitializeAsync (see integration-testing.md § 3)
await App.ResourceNotifications
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

1. **Resolve `BaseURL` from `App.GetEndpoint("web")`** — Aspire ports are dynamic. Hardcoding `http://localhost:5000` breaks the moment a second AppHost runs in parallel, or when CI uses `aspire start --isolated`.
2. **Gate on `Healthy`, not `Running`.** `WaitForResourceHealthyAsync` is mandatory in the fixture before any UI test executes. Resources without a registered health check effectively skip the wait — register one (`.WithHttpHealthCheck("/health/live")`).
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
    if (TestContext.CurrentTestOutcome == UnitTestOutcome.Passed) return;

    var runDir = Environment.GetEnvironmentVariable("BLAZTRAP_TEST_RUN_DIR")
                 ?? TestContext.TestRunResultsDirectory
                 ?? Path.GetTempPath();

    var artifactsDir = Path.Combine(runDir, "playwright");
    Directory.CreateDirectory(artifactsDir);

    var tracePath = Path.Combine(artifactsDir, $"trace-{TestContext.TestName}.zip");
    await Context.Tracing.StopAsync(new() { Path = tracePath });
    TestContext.AddResultFile(tracePath);

    var screenshotPath = Path.Combine(artifactsDir, $"{TestContext.TestName}.png");
    await Page.ScreenshotAsync(new() { Path = screenshotPath, FullPage = true });
    TestContext.AddResultFile(screenshotPath);
}
```

The artefact root resolution chain matches [integration-testing.md](integration-testing.md) § 9 (`BLAZTRAP_TEST_RUN_DIR` → `TestContext.TestRunResultsDirectory` → temp). Traces land alongside the per-resource Aspire logs, so a failed run gives one folder with everything.

View a trace: `pwsh bin/Debug/net10.0/playwright.ps1 show-trace trace-XXX.zip`.

## Parallelism

- A single shared `DistributedApplication` can serve many parallel `Page` / `Context` instances against the same `web` resource — Playwright contexts are independently isolated.
- The bottleneck is the app (CPU, DB connections), not Playwright.
- AppHost-level constraints from [integration-testing.md](integration-testing.md) § 7 still apply: MSTest 3.x parallelises classes by default, which races on the shared fixture's `BaseClassInit`. Keep the assembly-level `[assembly: Parallelize(Workers = 1, Scope = ExecutionScope.MethodLevel)]` for AppHost suites.
- For UI tests with destructive DB reset in `[TestInitialize]`, mark the class `[DoNotParallelize]` or scope state per tenant ID.

## Debugging

```powershell
$env:PWDEBUG=1
dotnet test --filter FullyQualifiedName~CheckoutUiTests
```

`PWDEBUG=1` forces `headless: false`, opens the Playwright Inspector, pauses on the first command. Locator picker available.

## Common Aspire-specific mistakes

| Mistake | Fix |
|---|---|
| `Page.GotoAsync("/")` without `BaseURL` set | Override `ContextOptions().BaseURL` from `App.GetEndpoint("web")`. Otherwise the URL resolves against `about:blank`. |
| Hardcoded `http://localhost:5000` | Always resolve dynamically from `GetEndpoint`. |
| Fixture started but UI flakes immediately | Missing `WaitForResourceHealthyAsync("web")` in the fixture, or the `web` resource has no registered health check. |
| Two test classes in the same closed generic create two browsers fighting for the same data | Move data setup to the shared fixture (see [test-seeding.md](test-seeding.md)) or scope per-test by tenant. |
| Trace files land in `bin/Debug/...` instead of the run root | Use the `BLAZTRAP_TEST_RUN_DIR` chain from § Trace. |

## Cross-references

- General Playwright (locators, `Expect`, network, CI matrix, codegen): `playwright-dotnet`.
- Aspire fixture skeleton: [integration-testing.md](integration-testing.md).
- Seeding the stack the UI runs on top of: [test-seeding.md](test-seeding.md).
- Live (Playwright .NET intro): https://playwright.dev/dotnet/docs/intro
- Live (Playwright + MSTest): https://playwright.dev/dotnet/docs/test-runners
- Live (Aspire testing): https://learn.microsoft.com/en-us/dotnet/aspire/testing/manage-app-host
