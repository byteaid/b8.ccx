# Actions and auto-waiting

Executing actions and understanding when Playwright waits automatically vs when an explicit wait is appropriate.

## Auto-waiting principle

Every Playwright action **auto-waits** for the element to be actionable before executing. No `WaitForSelector`, no `Task.Delay` before a click. The locator retries until conditions are met or the timeout expires.

## Form interaction

```csharp
// Fill — clears and types
await Page.GetByLabel("Email").FillAsync("user@example.com");

// Clear
await Page.GetByLabel("Email").ClearAsync();

// Type one keystroke at a time (only when the app reacts per keystroke — autocomplete)
await Page.GetByLabel("Search").PressSequentiallyAsync("play", new() { Delay = 100 });

// Checkboxes
await Page.GetByRole(AriaRole.Checkbox, new() { Name = "Terms" }).CheckAsync();
await Page.GetByRole(AriaRole.Checkbox, new() { Name = "Newsletter" }).UncheckAsync();
await Page.GetByRole(AriaRole.Checkbox, new() { Name = "Terms" }).SetCheckedAsync(true);

// Select
await Page.GetByLabel("Country").SelectOptionAsync("MX");
await Page.GetByLabel("Country").SelectOptionAsync(new SelectOptionValue { Label = "Mexico" });
await Page.GetByLabel("Tags").SelectOptionAsync(new[] { "urgent", "critical" });

// File upload
await Page.GetByLabel("Upload").SetInputFilesAsync("invoice.pdf");
await Page.GetByLabel("Upload").SetInputFilesAsync(new[] { "f1.pdf", "f2.pdf" });
await Page.GetByLabel("Upload").SetInputFilesAsync(Array.Empty<string>()); // clear
```

## Clicks

```csharp
await Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

await locator.ClickAsync(new() {
    Button       = MouseButton.Right,
    ClickCount   = 2,
    Modifiers    = new[] { KeyboardModifier.Control },
    Position     = new() { X = 10, Y = 20 },
    Force        = true,        // skip actionability checks (RARE)
    NoWaitAfter  = true,        // don't wait for post-click navigation (RARE)
    Delay        = 100,
});

await locator.DblClickAsync();
await Page.GetByTestId("tooltip-trigger").HoverAsync();
await Page.GetByLabel("Name").FocusAsync();
```

## Keyboard

```csharp
await Page.GetByLabel("Search").PressAsync("Enter");
await Page.PressAsync("body", "Control+a");
await Page.Keyboard.PressAsync("Shift+Tab");
await Page.Keyboard.TypeAsync("Hello World");
await locator.PressAsync("ArrowDown");
```

## Drag and drop

```csharp
await Page.GetByTestId("source").DragToAsync(Page.GetByTestId("target"));

await Page.GetByTestId("slider").DragToAsync(
    Page.GetByTestId("slider"),
    new() { TargetPosition = new() { X = 200, Y = 0 } });
```

## Reading content

```csharp
var text  = await locator.InnerTextAsync();
var all   = await locator.TextContentAsync();      // includes hidden
var value = await Page.GetByLabel("Email").InputValueAsync();
var href  = await locator.GetAttributeAsync("href");
int count = await Page.GetByRole(AriaRole.ListItem).CountAsync();
var texts      = await Page.GetByRole(AriaRole.ListItem).AllTextContentsAsync();
var innerTexts = await Page.GetByRole(AriaRole.ListItem).AllInnerTextsAsync();

bool visible = await locator.IsVisibleAsync(); // SNAPSHOT — does NOT retry
// Prefer Expect(locator).ToBeVisibleAsync() — see assertions.md
```

## Actionability checks per action

| Action | Visible | Stable | Enabled | Editable | Receives events |
|---|---|---|---|---|---|
| `ClickAsync` | Y | Y | Y | - | Y |
| `DblClickAsync` | Y | Y | Y | - | Y |
| `HoverAsync` | Y | Y | - | - | Y |
| `CheckAsync` | Y | Y | Y | - | Y |
| `FillAsync` | Y | - | Y | Y | - |
| `ClearAsync` | Y | - | Y | Y | - |
| `SelectOptionAsync` | Y | - | Y | - | - |
| `SetInputFilesAsync` | - | - | - | - | - |
| `PressAsync` | Y | - | - | - | - |
| `FocusAsync` | - | - | - | - | - |
| `DragToAsync` | Y | Y | - | - | Y |

Definitions:
- **Visible:** size > 0 and not `display: none` / `visibility: hidden`.
- **Stable:** not moving (animations finished).
- **Enabled:** no `disabled` attribute.
- **Editable:** is `<input>`, `<textarea>`, `<select>`, or `[contenteditable]`.
- **Receives events:** not covered by another element (overlay, modal).

## Explicit waits — when actually needed

In most cases you do NOT need explicit waits — locators + `Expect` cover everything. Exceptions:

### `WaitForURLAsync`

```csharp
await Page.GetByRole(AriaRole.Button, new() { Name = "Go" }).ClickAsync();
await Page.WaitForURLAsync("**/dashboard");
await Page.WaitForURLAsync(new Regex(".*/orders/\\d+"));
```

Prefer `Expect(Page).ToHaveURLAsync("**/dashboard")` — same effect plus assertion semantics.

### `WaitForLoadStateAsync`

```csharp
await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
// Load | DOMContentLoaded | NetworkIdle
```

Rarely necessary. Locators already wait for elements.

### `WaitForResponseAsync` / `WaitForRequestAsync`

```csharp
var responseTask = Page.WaitForResponseAsync("**/api/orders");
await Page.GetByRole(AriaRole.Button, new() { Name = "Load" }).ClickAsync();
var response = await responseTask;
Assert.AreEqual(200, response.Status);
```

**Order matters: register the wait BEFORE the action.** Inverted, the response is already emitted and the wait never resolves.

### `RunAndWaitForResponseAsync` — safe shortcut

```csharp
var response = await Page.RunAndWaitForResponseAsync(
    async () => await Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync(),
    "**/api/orders");
```

Registers the wait, runs the action, awaits the response — in correct order. Prefer this over manual `WaitForResponseAsync`.

### `WaitForSelectorAsync` — AVOID

```csharp
// Anti-pattern (Puppeteer leftover)
await Page.WaitForSelectorAsync("#myButton");
await Page.ClickAsync("#myButton");

// Correct
await Page.GetByRole(AriaRole.Button, new() { Name = "Submit" }).ClickAsync();
```

### `WaitForTimeoutAsync` — NEVER

```csharp
await Page.WaitForTimeoutAsync(2000); // NEVER
```

`#1 cause of flakiness`. If you need it, your locator or assertion is wrong. Replace with `Expect`-with-auto-retry or a network wait. Single documented exception: scrolling `<Virtualize>` lists in Blazor — see [blazor-wasm](blazor-wasm.md).

## Timeouts

```csharp
// Global
Page.SetDefaultTimeout(30_000);
Page.SetDefaultNavigationTimeout(60_000);

// Per-action
await locator.ClickAsync(new() { Timeout = 10_000 });
await Page.GotoAsync("/", new() { Timeout = 60_000 });

// Per-assertion
await Expect(locator).ToBeVisibleAsync(new() { Timeout = 15_000 });
```

### Recommended values

| Operation | Timeout |
|---|---|
| Default action (`Page.SetDefaultTimeout`) | 15s dev / 30s CI |
| Navigation (`GotoAsync`) | 30s |
| Default `Expect` assertion | 5s |
| `WaitForResourceHealthyAsync` (Aspire) | 60-180s |
| `[Timeout(ms)]` per-test (MSTest) | 30-120s |

## Sources

- https://playwright.dev/dotnet/docs/actionability
- https://playwright.dev/dotnet/docs/input
- https://playwright.dev/dotnet/docs/navigations
- https://playwright.dev/dotnet/docs/api/class-locator
