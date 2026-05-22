# Tracing and debugging

Capture traces / video / screenshots, debug interactively, generate code with codegen.

## Tracing — test replay

### Start / stop

```csharp
await Context.Tracing.StartAsync(new()
{
    Screenshots = true,
    Snapshots   = true,
    Sources     = true,
    Title       = "My test",
});

// ... actions ...

await Context.Tracing.StopAsync(new() { Path = "trace.zip" });
```

### Pattern: trace on-failure (recommended)

```csharp
[TestInitialize]
public async Task StartTrace()
{
    await Context.Tracing.StartAsync(new()
    {
        Screenshots = true, Snapshots = true, Sources = true,
    });
}

[TestCleanup]
public async Task StopTrace()
{
    if (TestContext.CurrentTestOutcome != UnitTestOutcome.Passed)
    {
        var dir = Path.Combine(
            TestContext.TestRunResultsDirectory ?? Path.GetTempPath(), "traces");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"trace-{TestContext.TestName}.zip");
        await Context.Tracing.StopAsync(new() { Path = path });
        TestContext.AddResultFile(path);
    }
    else
    {
        await Context.Tracing.StopAsync();
    }
}
```

Wrap in try/catch in long suites — `StopAsync` may throw if the test crashed before init.

### View

```bash
pwsh bin/Debug/net9.0/playwright.ps1 show-trace trace.zip
playwright show-trace trace.zip
```

Interactive UI: timeline, screenshots per step, DOM snapshots, network log, console log, source code.

## Video

### In `ContextOptions`

```csharp
public override BrowserNewContextOptions ContextOptions() => new()
{
    RecordVideoDir  = Path.Combine(TestContext.TestRunResultsDirectory ?? ".", "videos"),
    RecordVideoSize = new() { Width = 1280, Height = 800 },
};
```

### Save on failure

```csharp
[TestCleanup]
public async Task SaveVideo()
{
    var videoPath = await Page.Video!.PathAsync();
    if (TestContext.CurrentTestOutcome != UnitTestOutcome.Passed)
    {
        var dest = Path.Combine(
            Path.GetDirectoryName(videoPath)!, $"{TestContext.TestName}.webm");
        if (File.Exists(videoPath))
        {
            File.Move(videoPath, dest, overwrite: true);
            TestContext.AddResultFile(dest);
        }
    }
}
```

Video consumes disk and reduces performance. **Debug only — never default in CI.**

## Screenshots

### Manual

```csharp
await Page.ScreenshotAsync(new()
{
    Path     = "screenshot.png",
    FullPage = true,
    Type     = ScreenshotType.Png,
});

// Element-only
await Page.GetByTestId("chart").ScreenshotAsync(new() { Path = "chart.png" });
```

### On failure

```csharp
[TestCleanup]
public async Task CaptureScreenshotOnFailure()
{
    if (TestContext.CurrentTestOutcome != UnitTestOutcome.Passed)
    {
        var dir = Path.Combine(
            TestContext.TestRunResultsDirectory ?? Path.GetTempPath(), "screenshots");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{TestContext.TestName}.png");
        await Page.ScreenshotAsync(new() { Path = path, FullPage = true });
        TestContext.AddResultFile(path);
    }
}
```

## `PWDEBUG=1` — Playwright Inspector

```bash
# Bash
PWDEBUG=1 dotnet test --filter "FullyQualifiedName~LoginTests"

# PowerShell
$env:PWDEBUG=1; dotnet test --filter "FullyQualifiedName~LoginTests"
```

Effects:
- Forces **headed** browser (overrides `.runsettings`).
- Opens the **Inspector** with step-through, **locator picker** (hover the UI to see Playwright's locator), action log, browser console.
- Pauses before the first action.

When: locator doesn't match and you don't know why; pick the right locator interactively; inspect page state at a specific point.

## `Page.PauseAsync()` — programmatic breakpoint

```csharp
await Page.GotoAsync("/orders");
await Page.GetByRole(AriaRole.Button, new() { Name = "New" }).ClickAsync();
await Page.PauseAsync(); // opens Inspector if PWDEBUG=1; no-op otherwise
await Page.GetByLabel("Product").FillAsync("Widget");
```

Safe to leave in development code — no-op without `PWDEBUG`.

## Codegen

```bash
pwsh bin/Debug/net9.0/playwright.ps1 codegen https://example.com

pwsh bin/Debug/net9.0/playwright.ps1 codegen \
    --target csharp-mstest \
    --output tests/GeneratedTest.cs \
    https://localhost:5001
```

Produces a `.cs` file with `[TestMethod]` replaying clicks/fills/navigations.

Limits:
- Generates CSS locators — **rewrite as `GetByRole`/`GetByLabel`**.
- No assertions — add `Expect` manually.
- No fixture/state management.

Workflow: codegen → cherry-pick locators → rewrite as semantic locators → add assertions → delete the generated file.

## `PWDEBUG=console`

```bash
PWDEBUG=console dotnet test
```

Logs every Playwright operation to the browser console. Useful when the Inspector is unavailable (CI).

## VS Code / Visual Studio

### VS Code

1. Add `PWDEBUG=1` to `.vscode/launch.json`.
2. Set a breakpoint.
3. F5 — browser opens headed with Inspector.

### Visual Studio

1. Test Explorer → Debug test.
2. Properties → Debug → Environment Variables: `PWDEBUG=1`.
3. Inspector opens together with the VS debugger.

## Sources

- https://playwright.dev/dotnet/docs/trace-viewer
- https://playwright.dev/dotnet/docs/debug
- https://playwright.dev/dotnet/docs/codegen
- https://playwright.dev/dotnet/docs/screenshots
- https://playwright.dev/dotnet/docs/videos
