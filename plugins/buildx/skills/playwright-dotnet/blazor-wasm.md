# Blazor WebAssembly patterns

Blazor-specific behaviors Playwright doesn't handle out of the box.

## Bootstrap is asynchronous

Navigating to a Blazor WASM page returns a shell (loading spinner). The WebAssembly runtime downloads, initializes, then renders interactive components. Playwright has **no native API for "Blazor is ready"**.

### Solution 1: marker CSS class (recommended)

```razor
@* App.razor or MainLayout.razor *@
<body class="@(_isReady ? "blazor-ready" : "")">
    ...
</body>

@code {
    private bool _isReady;
    protected override void OnAfterRender(bool firstRender)
    {
        if (firstRender) { _isReady = true; StateHasChanged(); }
    }
}
```

```csharp
await Page.GotoAsync("/");
await Expect(Page.Locator("body.blazor-ready")).ToBeVisibleAsync(new() { Timeout = 30_000 });
// Now safe to interact
```

### Solution 2: wait for a known UI element

When the Blazor code can't be touched:

```csharp
await Page.GotoAsync("/dashboard");
await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard" }))
    .ToBeVisibleAsync(new() { Timeout = 30_000 });
```

Works because the heading only renders after WASM bootstrap. Fragile if the page content changes.

### Solution 3: `data-testid` on layout

```razor
@* MainLayout.razor *@
<div data-testid="app-loaded">
    @Body
</div>
```

```csharp
await Expect(Page.GetByTestId("app-loaded")).ToBeVisibleAsync(new() { Timeout = 30_000 });
```

**Solution 1 or 3 are most robust.** The 30s timeout covers cold WASM bootstrap.

## `data-testid` in components

Blazor renders normal HTML — `data-testid` works with no caveats.

```razor
@* OrderCard.razor *@
<div class="card" data-testid="order-card">
    <h5 data-testid="order-title">@Order.Title</h5>
    <span data-testid="order-total">@Order.Total.ToString("C")</span>
    <button data-testid="order-delete" @onclick="Delete">Delete</button>
</div>
```

```csharp
var card = Page.GetByTestId("order-card").Filter(new() { HasText = "Widget" });
await Expect(card.GetByTestId("order-total")).ToHaveTextAsync("$42.00");
await card.GetByTestId("order-delete").ClickAsync();
```

Convention: kebab-case. Prefer `GetByRole` when a semantic role exists.

## `EditForm` + `DataAnnotationsValidator`

Renders standard HTML: `<form>`, `<input>`, `<select>`, validation messages in `<div class="validation-message">`. Validation is client-side — no server round-trip — so Playwright can assert immediately after submit.

```razor
<EditForm Model="model" OnValidSubmit="Submit" data-testid="order-form">
    <DataAnnotationsValidator />
    <ValidationSummary />

    <div class="mb-3">
        <label for="customerName">Customer Name</label>
        <InputText id="customerName" @bind-Value="model.CustomerName" class="form-control" />
        <ValidationMessage For="() => model.CustomerName" />
    </div>

    <button type="submit" class="btn btn-primary">Submit</button>
</EditForm>
```

```csharp
await Page.GetByLabel("Customer Name").FillAsync("Alice");
await Page.GetByRole(AriaRole.Button, new() { Name = "Submit" }).ClickAsync();

// Trigger validation by clearing
await Page.GetByLabel("Customer Name").ClearAsync();
await Page.GetByRole(AriaRole.Button, new() { Name = "Submit" }).ClickAsync();
await Expect(Page.Locator(".validation-message"))
    .ToContainTextAsync("Customer Name is required");
```

## `<Virtualize>` — virtualized lists

Only renders visible items. Off-viewport items don't exist in the DOM.

### Scrolling pattern (one of the rare `WaitForTimeoutAsync` exceptions)

```csharp
var targetItem = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = "Item #500" });

int maxScrolls = 20;
for (int i = 0; i < maxScrolls; i++)
{
    if (await targetItem.IsVisibleAsync()) break;
    await Page.Mouse.WheelAsync(0, 500);
    await Page.WaitForTimeoutAsync(200); // exception — Virtualize renders async with no observable event
}

await Expect(targetItem).ToBeVisibleAsync();
```

### Better alternative — expose loaded count

If you control the component:

```razor
<span data-testid="loaded-count">@loadedItems.Count</span>
```

```csharp
await Expect(Page.GetByTestId("loaded-count")).ToHaveTextAsync("500");
```

## Client-side routing

`NavigationManager` does client-side routing — **no browser `load` event**. `RunAndWaitForNavigationAsync` does NOT resolve.

```csharp
// Does NOT work
await Page.RunAndWaitForNavigationAsync(async () =>
    await Page.GetByRole(AriaRole.Link, new() { Name = "Orders" }).ClickAsync());

// Works — wait for URL
await Page.GetByRole(AriaRole.Link, new() { Name = "Orders" }).ClickAsync();
await Expect(Page).ToHaveURLAsync("**/orders");

// Or wait for an element from the new page
await Page.GetByRole(AriaRole.Link, new() { Name = "Orders" }).ClickAsync();
await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Orders" })).ToBeVisibleAsync();
```

## SignalR reconnection

If the app uses SignalR (real-time notifications), the connection can drop and reconnect. Playwright doesn't observe this directly.

### Reconnect indicator

```csharp
await Expect(Page.GetByText("Reconnecting")).Not.ToBeVisibleAsync(new() { Timeout = 15_000 });
```

### Verify a SignalR message arrived

```csharp
await adminPage.GetByRole(AriaRole.Button, new() { Name = "Notify all" }).ClickAsync();

await Expect(userPage.GetByTestId("notification-toast"))
    .ToContainTextAsync("New notification", new() { Timeout = 10_000 });
```

## Pre-rendering (SSR) → Interactive WASM

The browser receives static HTML first; Blazor WASM takes over. During the transition:

1. Elements exist (static HTML) but are NOT interactive.
2. `@onclick` handlers don't work until WASM connects.
3. `FillAsync` may appear to work but Blazor binding hasn't activated.

### Safe pattern

```csharp
await Page.GotoAsync("/");
await Expect(Page.Locator("body.blazor-ready")).ToBeVisibleAsync(new() { Timeout = 30_000 });
// NOW interact
await Page.GetByLabel("Search").FillAsync("widget");
await Page.GetByRole(AriaRole.Button, new() { Name = "Search" }).ClickAsync();
```

If you can't add `body.blazor-ready`, wait for a marker only interactive Blazor can create:

```csharp
await Expect(Page.GetByTestId("interactive-marker")).ToBeVisibleAsync(new() { Timeout = 30_000 });
```

## Re-render races

`StateHasChanged()` re-renders. Two failure modes:

1. **Locator matches an element that's immediately re-rendered** → action fails: element destroyed.
2. **Click on a button that gets disabled after first click** → `ClickAsync` waits, but the button has disappeared.

### Fix: assertions first, actions later

```csharp
await Expect(Page.GetByRole(AriaRole.Row)).ToHaveCountAsync(5);
await Page.GetByRole(AriaRole.Row).First.GetByRole(AriaRole.Button).ClickAsync();
```

### Fix: stable `data-testid`

If Blazor re-renders and DOM structure changes, use a `data-testid` independent of position:

```razor
<tr data-testid="order-@order.Id">
    <td>@order.Name</td>
</tr>
```

```csharp
await Page.GetByTestId("order-42").GetByRole(AriaRole.Button).ClickAsync();
```

See also `dotnet-aspire` § playwright-testing for the host wiring.

## Sources

- https://playwright.dev/dotnet/docs/best-practices
- https://learn.microsoft.com/aspnet/core/blazor/test
- https://learn.microsoft.com/aspnet/core/blazor/components/virtualization
