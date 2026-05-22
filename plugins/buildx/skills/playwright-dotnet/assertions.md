# Assertions (Expect API)

Asserting element / page / response state. **Always `Expect` (auto-retry); never `IsVisibleAsync` + `Assert`.**

## Auto-retry vs snapshot

| Method | Auto-retry | When |
|---|---|---|
| `Expect(locator).ToBeVisibleAsync()` | Yes — retries up to default 5s | **Always — for any assertion about DOM state** |
| `locator.IsVisibleAsync()` | No — instant snapshot | Never as an assertion. Only for branching (`if`). |

```csharp
// CORRECT — auto-retry
await Expect(Page.GetByTestId("banner")).ToBeVisibleAsync();

// WRONG — snapshot, flaky
var isVisible = await Page.GetByTestId("banner").IsVisibleAsync();
Assert.IsTrue(isVisible);
```

## Locator assertions

### Visibility / presence

```csharp
await Expect(locator).ToBeVisibleAsync();      // visible in viewport
await Expect(locator).ToBeHiddenAsync();        // hidden, display:none, or absent
await Expect(locator).ToBeAttachedAsync();      // exists in DOM
await Expect(locator).Not.ToBeAttachedAsync();  // not in DOM
await Expect(locator).ToBeInViewportAsync();    // inside viewport
```

### State

```csharp
await Expect(locator).ToBeEnabledAsync();
await Expect(locator).ToBeDisabledAsync();
await Expect(locator).ToBeEditableAsync();
await Expect(locator).Not.ToBeEditableAsync();   // readonly
await Expect(locator).ToBeFocusedAsync();
await Expect(locator).ToBeCheckedAsync();
await Expect(locator).Not.ToBeCheckedAsync();
await Expect(locator).ToBeEmptyAsync();
```

### Text

```csharp
// Substring
await Expect(locator).ToContainTextAsync("Welcome");
await Expect(locator).ToContainTextAsync(new Regex("Total: \\$\\d+"));

// Exact
await Expect(locator).ToHaveTextAsync("Welcome back, Alice");
await Expect(locator).ToHaveTextAsync(new Regex("^Welcome back, \\w+$"));

// Multiple elements
await Expect(Page.GetByRole(AriaRole.ListItem))
    .ToHaveTextAsync(new[] { "Item 1", "Item 2", "Item 3" });
```

### Input values

```csharp
await Expect(Page.GetByLabel("Email")).ToHaveValueAsync("user@example.com");
await Expect(Page.GetByLabel("Email")).ToHaveValueAsync(new Regex(".*@example\\.com"));
await Expect(Page.GetByLabel("Tags")).ToHaveValuesAsync(new[] { "urgent", "critical" });
```

### Attributes / CSS

```csharp
await Expect(locator).ToHaveAttributeAsync("href", "/dashboard");
await Expect(locator).ToHaveAttributeAsync("href", new Regex("^/dashboard"));
await Expect(locator).ToHaveIdAsync("main-nav");
await Expect(locator).ToHaveClassAsync("btn btn-primary");
await Expect(locator).ToContainClassAsync("btn-primary");        // 1.51+
await Expect(locator).ToHaveCSSAsync("color", "rgb(255, 0, 0)");
```

### Count

```csharp
await Expect(Page.GetByRole(AriaRole.ListItem)).ToHaveCountAsync(5);
await Expect(Page.GetByRole(AriaRole.Row)).ToHaveCountAsync(0); // empty table
```

### Accessibility (1.44+)

```csharp
// Aria snapshot
await Expect(locator).ToMatchAriaSnapshotAsync(@"
  - heading ""Dashboard"" [level=1]
  - list:
    - listitem: ""Item 1""
    - listitem: ""Item 2""
");

// Role (1.51+)
await Expect(locator).ToHaveRoleAsync(AriaRole.Button);

// Accessible error message (1.50+)
await Expect(locator).ToHaveAccessibleErrorMessageAsync("Email is required");
```

## Page assertions

```csharp
await Expect(Page).ToHaveURLAsync("**/dashboard");
await Expect(Page).ToHaveURLAsync(new Regex("/orders/\\d+"));
await Expect(Page).ToHaveTitleAsync("My App - Dashboard");
await Expect(Page).ToHaveTitleAsync(new Regex(".*Dashboard"));
```

## APIResponse assertions

```csharp
var response = await Page.Request.GetAsync("/api/orders");
await Expect(response).ToBeOKAsync(); // 200-299
```

## Negation

Every assertion supports `.Not`. Negation also retries — it waits until the negated condition holds.

```csharp
await Expect(locator).Not.ToBeVisibleAsync();
await Expect(locator).Not.ToHaveTextAsync("Error");
await Expect(locator).Not.ToBeCheckedAsync();
await Expect(Page).Not.ToHaveURLAsync("**/login");
```

## Per-assertion timeout

```csharp
await Expect(locator).ToBeVisibleAsync(new() { Timeout = 15_000 });
await Expect(Page).ToHaveURLAsync("**/dashboard", new() { Timeout = 30_000 });
```

## Soft assertions

Don't fail immediately — accumulate errors, report at end:

```csharp
await Expect(locator).ToBeVisibleAsync();                          // hard
await SoftExpect(locator).ToHaveTextAsync("Hello");                // soft
await SoftExpect(locator).ToHaveAttributeAsync("class", "active"); // soft
// Soft errors thrown at test end
```

Use when verifying multiple independent properties and you want all failures at once. Don't use when failures depend sequentially (login fail → dashboard checks pointless).

## Assertion-as-wait

`Expect` doubles as an implicit wait. Better than `WaitForSelectorAsync` because: (a) declarative — says WHAT you expect; (b) intelligent retry; (c) clear failure message ("expected 5 items, got 0").

```csharp
await Expect(Page.GetByRole(AriaRole.Row)).ToHaveCountAsync(5); // wait for table
await Page.GetByRole(AriaRole.Row).First.GetByRole(AriaRole.Button).ClickAsync();
```

## Common errors

1. `Assert.IsTrue(await locator.IsVisibleAsync())` — snapshot, no retry. Use `Expect(...).ToBeVisibleAsync()`.
2. **Forgotten `await` on assertion** — silently passes (returns Task without observing).
3. **Default 5s too short for Blazor WASM cold-boot** — raise to 15s for the bootstrap assertion only.
4. `ToHaveTextAsync("Hello")` whitespace mismatch — Playwright normalizes whitespace, but is case-sensitive. Use regex if text varies.
5. **Negation without adequate timeout** — `Not.ToBeVisibleAsync()` waits up to 5s for disappearance. Animations may need higher.

## Sources

- https://playwright.dev/dotnet/docs/test-assertions
- https://playwright.dev/dotnet/docs/api/class-locatorassertions
- https://playwright.dev/dotnet/docs/api/class-pageassertions
- https://playwright.dev/dotnet/docs/api/class-apiresponseassertions
