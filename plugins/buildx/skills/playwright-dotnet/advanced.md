# Advanced patterns

POM, parallel execution, device/locale emulation, file ops, accessibility, iframes, API testing.

## Page Object Model

### When

- Same page is tested from multiple test classes.
- Many repeated locators.
- Encapsulating complex multi-step navigation.

### When NOT

- A locator is used once — leave it inline.
- Trivial pages — POM is overhead without value.
- Playwright locators already encapsulate auto-wait and retry — they're "POM-like".

### Example

```csharp
// PageObjects/OrdersPage.cs
public sealed class OrdersPage
{
    private readonly IPage _page;
    public OrdersPage(IPage page) => _page = page;

    // Locators as => properties — lazy, always fresh
    private ILocator NewOrderButton => _page.GetByRole(AriaRole.Button, new() { Name = "New Order" });
    private ILocator OrderRows      => _page.GetByRole(AriaRole.Row);
    private ILocator SearchInput    => _page.GetByLabel("Search orders");

    public async Task GotoAsync()
    {
        await _page.GotoAsync("/orders");
        await Assertions.Expect(OrderRows.First).ToBeVisibleAsync();
    }

    public async Task SearchAsync(string query)
    {
        await SearchInput.FillAsync(query);
        await SearchInput.PressAsync("Enter");
    }

    public async Task CreateOrderAsync(string customer, string product)
    {
        await NewOrderButton.ClickAsync();
        await _page.GetByLabel("Customer").FillAsync(customer);
        await _page.GetByLabel("Product").FillAsync(product);
        await _page.GetByRole(AriaRole.Button, new() { Name = "Submit" }).ClickAsync();
    }

    public ILocator OrderRow(string orderId) =>
        OrderRows.Filter(new() { HasText = orderId });

    public async Task<int> CountAsync() => await OrderRows.CountAsync();
}

// Tests/OrdersTests.cs
[TestClass]
public class OrdersTests : PageTest
{
    private OrdersPage _orders = null!;
    [TestInitialize] public void SetupPage() => _orders = new OrdersPage(Page);

    [TestMethod]
    public async Task CreateOrder_AppearsInList()
    {
        await _orders.GotoAsync();
        await _orders.CreateOrderAsync("Alice", "Widget");
        await Expect(_orders.OrderRow("Alice")).ToBeVisibleAsync();
    }
}
```

### Rules

- POM classes do NOT inherit `PageTest` — they receive `IPage` via constructor.
- Locators as `=>` properties (not fields) — always fresh.
- POM encapsulates navigation + locators only; assertions live in tests.
- One POM class per page route.

## Parallel execution

### Thread safety

| Object | Thread-safe | Note |
|---|---|---|
| `IPlaywright` | Yes | Singleton OK |
| `IBrowser` | Yes | Shareable |
| `IBrowserContext` | No | One per thread/test |
| `IPage` | No | One per thread/test |

### MSTest config

```csharp
// AssemblyInfo.cs
[assembly: Parallelize(Workers = 0, Scope = ExecutionScope.ClassLevel)]
```

- `Workers = 0` → auto (cores).
- `ClassLevel` → classes parallel; tests within a class sequential.
- `MethodLevel` → every test parallel (faster, full isolation required).

### With Aspire

A single shared `DistributedApplication` + per-test contexts → no cookie/localStorage collision. Bottleneck is the app (CPU, DB connections), not Playwright. Reduce `<Workers>` if the app saturates.

## Device and locale emulation

### Mobile

```csharp
public override BrowserNewContextOptions ContextOptions()
{
    var iphone = Playwright.Devices["iPhone 14"];
    return new(iphone)
    {
        BaseURL = "https://localhost:5001",
        IgnoreHTTPSErrors = true,
    };
}
```

Available: `iPhone 14`, `iPhone 14 Pro Max`, `Pixel 7`, `iPad Pro 11`, `Galaxy S9+`, etc. Full list via `Playwright.Devices`.

### Manual options

```csharp
public override BrowserNewContextOptions ContextOptions() => new()
{
    ViewportSize       = new() { Width = 375, Height = 812 },
    IsMobile           = true,
    HasTouch           = true,
    DeviceScaleFactor  = 3,
    Locale             = "es-MX",
    TimezoneId         = "America/Mexico_City",
    Geolocation        = new() { Latitude = 19.4326, Longitude = -99.1332 },
    Permissions        = new[] { "geolocation" },
    ColorScheme        = ColorScheme.Dark,
    ReducedMotion      = ReducedMotion.Reduce,
    ForcedColors       = ForcedColors.Active,
    Offline            = true,
    UserAgent          = "CustomBot/1.0",
    HttpCredentials    = new() { Username = "user", Password = "pass" },
};
```

## File upload

```csharp
await Page.GetByLabel("Upload invoice").SetInputFilesAsync("testdata/invoice.pdf");
await Page.GetByLabel("Attachments").SetInputFilesAsync(new[]
{
    "testdata/file1.pdf",
    "testdata/file2.png",
});
await Page.GetByLabel("Upload").SetInputFilesAsync(Array.Empty<string>()); // clear

// In-memory
await Page.GetByLabel("Upload").SetInputFilesAsync(new FilePayload
{
    Name     = "test.txt",
    MimeType = "text/plain",
    Buffer   = Encoding.UTF8.GetBytes("Hello World"),
});
```

## File download

```csharp
var download = await Page.RunAndWaitForDownloadAsync(async () =>
{
    await Page.GetByRole(AriaRole.Link, new() { Name = "Download report" }).ClickAsync();
});

await download.SaveAsAsync("downloads/report.pdf");
var path     = await download.PathAsync();
var stream   = await download.CreateReadStreamAsync();
var filename = download.SuggestedFilename;
```

## Accessibility

### `GetByRole` is accessibility-first

Implicitly tests correct ARIA roles. A `<div>` acting as a button without `role="button"` is invisible to `GetByRole(AriaRole.Button)` — surfaces the a11y bug.

### Aria snapshot (1.44+)

```csharp
await Expect(Page.GetByTestId("nav")).ToMatchAriaSnapshotAsync(@"
  - navigation:
    - link ""Home""
    - link ""Orders""
    - link ""Settings""
");
```

### Forced colors

```csharp
public override BrowserNewContextOptions ContextOptions() => new()
{
    ForcedColors = ForcedColors.Active,
};
```

### Tab order

```csharp
await Page.GetByLabel("Email").FocusAsync();
await Page.Keyboard.PressAsync("Tab");
await Expect(Page.GetByLabel("Password")).ToBeFocusedAsync();
await Page.Keyboard.PressAsync("Tab");
await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Sign in" })).ToBeFocusedAsync();
```

### axe-core injection

```csharp
await Page.AddScriptTagAsync(new() { Url = "https://cdn.jsdelivr.net/npm/axe-core@4/axe.min.js" });
var results    = await Page.EvaluateAsync<JsonElement>("async () => await axe.run()");
var violations = results.GetProperty("violations");
Assert.AreEqual(0, violations.GetArrayLength(), $"Accessibility violations: {violations}");
```

## Iframes and shadow DOM

```csharp
// Iframe
var frame = Page.FrameLocator("#payment-iframe");
await frame.GetByRole(AriaRole.Textbox, new() { Name = "Card number" }).FillAsync("4242...");

// Nested
var inner = Page.FrameLocator("iframe.outer").FrameLocator("iframe.inner");
await inner.GetByText("Hello").ClickAsync();
```

`open` shadow DOM: pierced automatically by all `GetBy*` locators. `closed`: inaccessible.

## API testing without a browser

`APIRequestContext` for pure HTTP tests:

```csharp
var api = await Playwright.APIRequest.NewContextAsync(new()
{
    BaseURL = "https://localhost:5001",
    ExtraHTTPHeaders = new Dictionary<string, string>
    {
        ["Authorization"] = "Bearer token123"
    }
});

var response = await api.GetAsync("/api/orders");
await Expect(response).ToBeOKAsync();
var orders = await response.JsonAsync();

var created = await api.PostAsync("/api/orders", new()
{
    DataObject = new { customer = "Alice", product = "Widget" }
});
Assert.AreEqual(201, created.Status);

await api.DisposeAsync();
```

**With Aspire:** prefer `app.CreateHttpClient("api")` — resolves service discovery and has resilience handlers wired. Use `APIRequestContext` only when combining browser AND API checks in one test.

## Sources

- https://playwright.dev/dotnet/docs/pom
- https://playwright.dev/dotnet/docs/emulation
- https://playwright.dev/dotnet/docs/downloads
- https://playwright.dev/dotnet/docs/input#upload-files
- https://playwright.dev/dotnet/docs/accessibility-testing
- https://playwright.dev/dotnet/docs/frames
- https://playwright.dev/dotnet/docs/api-testing
