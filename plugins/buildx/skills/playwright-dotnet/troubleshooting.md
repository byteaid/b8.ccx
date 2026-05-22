# Troubleshooting (15 problems)

`Symptom → Cause → Fix`. Read top-down — the most common issues are first.

## 1. Timeout in CI but passes locally

**Symptom:** `TimeoutError: Timeout 30000ms exceeded` only in CI.

**Cause:** (a) CI runner has less CPU/memory → Blazor WASM bootstrap takes longer. (b) Missing system deps on Linux (`libatk-bridge`, `libgbm`). (c) Server not ready when test navigated.

**Fix:**
- `playwright.ps1 install --with-deps` in pipeline.
- Increase timeouts in CI: `Page.SetDefaultTimeout(30_000)` (15s local, 30s CI).
- `WaitForResourceHealthyAsync` before tests — see `dotnet-aspire` § playwright-testing.
- `BaseURL` from `app.GetEndpoint("web")`, not hardcoded.

## 2. `Browser closed` / `Target page, context or browser has been closed`

**Symptom:** mid-test `PlaywrightException: Browser has been closed`.

**Cause:** (a) MSTest `[Timeout(ms)]` killed the run. (b) A previous test closed the shared browser by mistake. (c) OOM in CI.

**Fix:**
- Raise `[Timeout]`.
- `[TestCleanup]` must close only the test's context/page — never the shared browser.
- Each Chromium ≈ 200-400MB. Reduce `<Workers>` if memory-bound.

## 3. `playwright.ps1 install` fails with permissions

**Symptom:** `Permission denied` / `Access is denied`.

**Cause:** PowerShell execution policy or filesystem permissions.

**Fix:**
```bash
pwsh -ExecutionPolicy Bypass -File bin/Debug/net10.0/playwright.ps1 install
# or
dotnet tool install --global Microsoft.Playwright.CLI
playwright install --with-deps
```

## 4. Tests pass on Chromium but fail on Firefox/WebKit

**Symptom:** Green on default Chromium, red on Firefox or WebKit.

**Cause:** Real cross-browser differences — rendering, CSS support, JS APIs. Firefox stricter on CORS; WebKit different animation timing.

**Fix:** Investigate the real bug. If targeting only Chromium/MSEdge, document in `.runsettings` and skip the others. For animation issues set `ReducedMotion = "reduce"` in `ContextOptions`.

## 5. Strict-mode violation: `locator resolved to N elements`

**Symptom:** `strict mode violation: GetByRole(AriaRole.Button, { Name = "Delete" }) resolved to 3 elements`.

**Cause:** Locator is ambiguous.

**Fix:**
```csharp
// Scope to a parent
var row = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = "Order #42" });
await row.GetByRole(AriaRole.Button, new() { Name = "Delete" }).ClickAsync();

// Or First/Nth if order is meaningful
await Page.GetByRole(AriaRole.Button, new() { Name = "Delete" }).First.ClickAsync();
```

## 6. Blazor re-renders during the action

**Symptom:** `ClickAsync` fails with `element is not stable` or `element was detached from DOM`.

**Cause:** `StateHasChanged()` re-renders between locator-finds-element and click. Element destroyed and recreated.

**Fix:** Wait for stable UI BEFORE acting:
```csharp
await Expect(Page.GetByRole(AriaRole.Row)).ToHaveCountAsync(5);
await Page.GetByRole(AriaRole.Row).First.ClickAsync();
```
Use position-independent `data-testid` (`order-@order.Id`). See [blazor-wasm](blazor-wasm.md).

## 7. Black screenshots in headless Linux

**Symptom:** Captured screenshots are entirely black.

**Cause:** Missing GPU libs in the Linux container. Headless Chromium uses software rendering but needs certain libs.

**Fix:**
- `playwright.ps1 install --with-deps`.
- Or use `mcr.microsoft.com/playwright/dotnet:v1.51.0-noble`.
- Last resort: `args: ["--disable-gpu"]` in launch options.

## 8. `net::ERR_CONNECTION_REFUSED` when server hasn't started

**Symptom:** `Page.GotoAsync` → `net::ERR_CONNECTION_REFUSED`.

**Cause:** Aspire host not ready when test navigated.

**Fix:** `WaitForResourceHealthyAsync` in `[AssemblyInitialize]` BEFORE creating Playwright pages — see `dotnet-aspire` § playwright-testing.

## 9. Playwright doesn't detect Blazor client-side navigation

**Symptom:** `RunAndWaitForNavigationAsync` never resolves after a `<NavLink>` click.

**Cause:** `NavigationManager` does client-side routing — no browser `load` event.

**Fix:** Don't use `WaitForNavigationAsync`. Use `Expect(Page).ToHaveURLAsync("**/target")` after the click. See [blazor-wasm](blazor-wasm.md).

## 10. File upload fails in CI

**Symptom:** `SetInputFilesAsync("myfile.pdf")` → `File not found`.

**Cause:** Relative path resolves against the working directory, which differs in CI.

**Fix:**
```csharp
var filePath = Path.Combine(AppContext.BaseDirectory, "TestData", "myfile.pdf");
await Page.GetByLabel("Upload").SetInputFilesAsync(filePath);
```
Mark the file `<Content>` or `<None CopyToOutputDirectory="PreserveNewest">` in csproj.

## 11. `WaitForResponseAsync` never resolves

**Symptom:** Test hangs on `WaitForResponseAsync`.

**Cause:** Response was emitted BEFORE the wait was registered.

**Fix:**
```csharp
// WRONG — response already gone
await Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
var resp = await Page.WaitForResponseAsync("**/api/orders");

// RIGHT — register before
var respTask = Page.WaitForResponseAsync("**/api/orders");
await Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
var resp = await respTask;

// BETTER — use helper
var resp2 = await Page.RunAndWaitForResponseAsync(
    async () => await Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync(),
    "**/api/orders");
```

## 12. Memory leaks in large suites

**Symptom:** 200+ test suite slows and OOMs.

**Cause:** Contexts/pages not disposed.

**Fix:** `PageTest` lifecycle is automatic. With `BrowserTest`/`ContextTest` ensure `await context.CloseAsync()` in `[TestCleanup]`. Verify you're not accumulating event handlers on `Page.Console` or `Page.Response`.

## 13. Trace file corrupt or empty

**Symptom:** `show-trace` reports "Invalid trace file" or zip is empty.

**Cause:** `Tracing.StopAsync` not called (test crashed before) or called without `Path`.

**Fix:** Wrap `StopAsync` in try/catch in `[TestCleanup]`:
```csharp
[TestCleanup]
public async Task Cleanup()
{
    try
    {
        await Context.Tracing.StopAsync(new()
        {
            Path = Path.Combine("traces", $"{TestContext.TestName}.zip")
        });
    }
    catch { /* best effort */ }
}
```

## 14. MSTest `[Timeout]` doesn't work with Playwright assertions

**Symptom:** Test exceeds `[Timeout(30000)]` but the Playwright assertion keeps waiting.

**Cause:** MSTest `[Timeout]` cancels `TestContext.CancellationTokenSource`, but Playwright assertions don't observe that token.

**Fix:** Set explicit timeouts on assertions, shorter than the test `[Timeout]`:
```csharp
await Expect(locator).ToBeVisibleAsync(new() { Timeout = 10_000 });
```

## 15. Version mismatch between `Microsoft.Playwright.MSTest` and `Microsoft.Playwright`

**Symptom:** `TypeLoadException` or `MissingMethodException` at runtime.

**Cause:** Two packages on different versions. `Microsoft.Playwright.MSTest` has a transitive pin on a specific `Microsoft.Playwright`.

**Fix:** Don't pin `Microsoft.Playwright` explicitly — it comes transitively:
```xml
<PackageReference Include="Microsoft.Playwright.MSTest" Version="1.51.0" />
```
If both must be explicit, keep them in sync:
```bash
dotnet add package Microsoft.Playwright.MSTest --version 1.51.0
dotnet add package Microsoft.Playwright       --version 1.51.0
```

## Quick diagnostic checklist

1. Browsers installed? `pwsh playwright.ps1 install --with-deps`
2. Server ready? `WaitForResourceHealthyAsync` before tests
3. Correct `BaseURL`? `app.GetEndpoint("web")`, not hardcoded
4. Locator matches one only? `await locator.CountAsync()` in debug
5. Assertion uses `Expect`? Not `IsVisibleAsync()` + `Assert.IsTrue`
6. Any `WaitForTimeoutAsync` / `Thread.Sleep`? Remove and fix the locator
7. Tests dispose contexts? Use `PageTest` (auto) or `CloseAsync` in cleanup
8. Versions synchronized? `dotnet list package | findstr Playwright`

## Sources

- https://playwright.dev/dotnet/docs/debug
- https://playwright.dev/dotnet/docs/best-practices
- https://github.com/microsoft/playwright-dotnet/issues
