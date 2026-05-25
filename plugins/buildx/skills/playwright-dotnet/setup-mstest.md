# Setup: MSTest + Playwright .NET

Initial setup: NuGet packages, browser install, `.runsettings`, env vars, base classes.

## NuGet

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Playwright.MSTest" Version="1.51.0" />
  <!-- Microsoft.Playwright is a transitive dependency. Do NOT pin it explicitly. -->
</ItemGroup>
```

- 1.51.x is current as of 2026-04. TFMs: `net8.0`, `net9.0`, `net10.0` (`netstandard2.0` dropped in 1.47).
- Use `Microsoft.Playwright.MSTest` only — never `Microsoft.Playwright.NUnit`, never both.
- The two packages MUST be on the same version. Pinning both is allowed only if you keep them synchronized.

## Browser install

After the first `dotnet build`:

```bash
# All browsers
pwsh bin/Debug/net9.0/playwright.ps1 install

# Chromium only (fastest local dev)
pwsh bin/Debug/net9.0/playwright.ps1 install chromium

# MSEdge channel
pwsh bin/Debug/net9.0/playwright.ps1 install msedge

# CI on Linux — REQUIRED
pwsh bin/Debug/net9.0/playwright.ps1 install --with-deps

# Or via the global tool
dotnet tool install --global Microsoft.Playwright.CLI
playwright install --with-deps
```

`--with-deps` on Linux is mandatory; without it Chromium fails at runtime with missing `libatk-bridge` / `libgbm`. Adjust the path to match your TFM and `Debug`/`Release` folder.

## Base classes (`Microsoft.Playwright.MSTest`)

| Class | Provides | Use for |
|---|---|---|
| `PageTest` | `Browser`, `Context`, `Page` (fresh per test) | **Default — ~90% of UI tests** |
| `ContextTest` | `Browser`, `Context` (no automatic Page) | Multi-page scenarios (tabs, popups) |
| `BrowserTest` | `Browser` only | Multi-context (admin + user, multi-role) |

### `PageTest` — canonical shape

```csharp
using Microsoft.Playwright.MSTest;

[TestClass]
public class MyTests : PageTest
{
    [TestMethod]
    public async Task ClickButton()
    {
        await Page.GotoAsync("https://example.com");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Submit" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync("**/success");
    }
}
```

Per-test lifecycle: new `IBrowserContext` → new `IPage` → run test → dispose page+context.

### Override `ContextOptions()`

```csharp
public override BrowserNewContextOptions ContextOptions() => new()
{
    BaseURL = "https://localhost:5001",
    IgnoreHTTPSErrors = true,
    ViewportSize = new() { Width = 1280, Height = 800 },
    Locale = "en-US",
    TimezoneId = "America/Mexico_City",
    ColorScheme = ColorScheme.Light,
    ReducedMotion = ReducedMotion.Reduce,
    RecordVideoDir = Path.Combine(TestArtifacts.RunDir(TestContext), "videos"),
    RecordVideoSize = new() { Width = 1280, Height = 800 },
};
```

### `ContextTest` — multi-page

```csharp
[TestClass]
public class MultiPageTests : ContextTest
{
    [TestMethod]
    public async Task TwoTabsScenario()
    {
        var page1 = await Context.NewPageAsync();
        var page2 = await Context.NewPageAsync();
    }
}
```

### `BrowserTest` — multi-context

```csharp
[TestClass]
public class MultiRoleTests : BrowserTest
{
    [TestMethod]
    public async Task AdminAndUserView()
    {
        var adminCtx = await Browser.NewContextAsync(new() { StorageStatePath = "admin-state.json" });
        var userCtx  = await Browser.NewContextAsync(new() { StorageStatePath = "user-state.json"  });
        var adminPage = await adminCtx.NewPageAsync();
        var userPage  = await userCtx.NewPageAsync();
    }
}
```

See [auth-storage](auth-storage.md) for the StorageState pattern.

## `.runsettings` — full template

```xml
<?xml version="1.0" encoding="utf-8"?>
<RunSettings>
  <Playwright>
    <BrowserName>chromium</BrowserName>
    <LaunchOptions>
      <Headless>true</Headless>
      <Channel>msedge</Channel>
      <SlowMo>0</SlowMo>
      <Timeout>30000</Timeout>
    </LaunchOptions>
    <ContextOptions>
      <ViewportSize>
        <Width>1280</Width>
        <Height>720</Height>
      </ViewportSize>
      <IgnoreHTTPSErrors>true</IgnoreHTTPSErrors>
      <Locale>en-US</Locale>
      <TimezoneId>America/Mexico_City</TimezoneId>
      <ColorScheme>light</ColorScheme>
      <ReducedMotion>reduce</ReducedMotion>
    </ContextOptions>
    <ExpectTimeout>5000</ExpectTimeout>
  </Playwright>
  <MSTest>
    <Parallelize>
      <Workers>0</Workers>
      <Scope>ClassLevel</Scope>
    </Parallelize>
  </MSTest>
</RunSettings>
```

Wire it up in the csproj:

```xml
<PropertyGroup>
  <RunSettingsFilePath>$(MSBuildThisFileDirectory).runsettings</RunSettingsFilePath>
</PropertyGroup>
```

Or pass it explicitly: `dotnet test --settings .runsettings`.

## Environment variables

| Var | Effect |
|---|---|
| `PWDEBUG=1` | Opens Playwright Inspector, pauses before each action, forces headed |
| `PWDEBUG=console` | Logs Playwright operations to the browser console |
| `BROWSER=firefox` | Overrides `BrowserName` |
| `HEADED=1` | Headed mode |
| `SLOWMO=500` | Pause (ms) between actions |
| `CI=true` | Auto-detected on GH Actions / Azure DevOps |
| `PLAYWRIGHT_BROWSERS_PATH=0` | Use browsers from package directory (default) |
| `PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1` | Skip browser download on install |

## Bundled browsers (1.51.x)

| Browser | Bundled version | Channel flag |
|---|---|---|
| Chromium | ~133.x | `chromium` (default) |
| Firefox | ~134.x | `firefox` |
| WebKit | ~18.4 | `webkit` |
| Google Chrome | system stable | `channel: "chrome"` |
| Microsoft Edge | system stable | `channel: "msedge"` |

`chrome` / `msedge` use the system binary, NOT the bundled Chromium. **Default channel is `msedge`.** In CI: `playwright.ps1 install msedge`.

## Aspire integration (NOT here)

The per-class `[ClassInitialize]` mount that builds `DistributedApplication`, the `ContextOptions().BaseURL = _baseUrl` override (captured from `_app.GetEndpoint("web")` after `WaitForResourceHealthyAsync`), and the `TestArtifacts` helper live in `dotnet-aspire` § playwright-testing and `dotnet-testing` § mstest-integration. This skill covers only the Playwright API.

## Recent changes (1.40 → 1.51)

| Version | Change |
|---|---|
| 1.43 | `ClockAsync` API (manipulate time) |
| 1.44 | .NET 7 dropped, `maxRetries` on `RouteAsync`, `Locator.AriaSnapshotAsync` |
| 1.45 | `Expect(locator).ToMatchAriaSnapshotAsync()` |
| 1.46 | `WebSocketRoute` |
| 1.47 | `netstandard2.0` dropped — requires .NET 8+ |
| 1.50 | `Locator.SelectTextAsync`, `ToHaveAccessibleErrorMessageAsync` |
| 1.51 | `ToHaveRoleAsync()`, `ToContainClass()` |

## Sources

- https://playwright.dev/dotnet/docs/intro
- https://playwright.dev/dotnet/docs/test-runners
- https://www.nuget.org/packages/Microsoft.Playwright.MSTest
