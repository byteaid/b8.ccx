# Network interception

Mock APIs, block requests, simulate errors, record/replay HAR.

## `Page.RouteAsync` — intercept

### Mock an API

```csharp
await Page.RouteAsync("**/api/orders", async route =>
{
    await route.FulfillAsync(new()
    {
        Status      = 200,
        ContentType = "application/json",
        Body = JsonSerializer.Serialize(new[]
        {
            new { Id = 1, Status = "Pending", Total = 42.00 },
            new { Id = 2, Status = "Shipped", Total = 18.50 },
        }),
    });
});

await Page.GotoAsync("/orders");
```

### Server error

```csharp
await Page.RouteAsync("**/api/orders", async route =>
{
    await route.FulfillAsync(new()
    {
        Status      = 500,
        ContentType = "application/json",
        Body        = """{"error": "Internal server error"}""",
    });
});
```

### Network error

```csharp
await Page.RouteAsync("**/api/orders", async route =>
{
    await route.AbortAsync("connectionfailed");
    // Options: connectionfailed, connectionrefused, connectionreset,
    //          internetdisconnected, namenotresolved, timedout, failed
});
```

### Modify request (continue with changes)

```csharp
await Page.RouteAsync("**/api/orders", async route =>
{
    var headers = new Dictionary<string, string>(route.Request.Headers)
    {
        ["X-Custom-Header"] = "test-value"
    };
    await route.ContinueAsync(new() { Headers = headers });
});
```

### Modify response (intercept and alter)

```csharp
await Page.RouteAsync("**/api/orders", async route =>
{
    var response = await route.FetchAsync();           // hit real server
    var body     = await response.JsonAsync();
    var orders   = body!.Deserialize<List<OrderDto>>()!;
    orders.Add(new OrderDto(999, "Injected", 0));

    await route.FulfillAsync(new()
    {
        Response = response,
        Body     = JsonSerializer.Serialize(orders),
    });
});
```

### Block analytics / images / ads

```csharp
await Page.RouteAsync("**/*.{png,jpg,jpeg,gif,svg,ico}", route => route.AbortAsync());
await Page.RouteAsync("**/analytics/**",                 route => route.AbortAsync());
await Page.RouteAsync("**/google-analytics.com/**",      route => route.AbortAsync());
```

### Unregister

```csharp
await Page.UnrouteAsync("**/api/orders");

// With specific handler
Func<IRoute, Task> handler = route => route.AbortAsync();
await Page.RouteAsync("**/api/orders", handler);
await Page.UnrouteAsync("**/api/orders", handler);
```

### Glob patterns

| Pattern | Matches |
|---|---|
| `**/api/orders` | Any URL ending `/api/orders` |
| `**/api/orders/*` | `/api/orders/123` only (one segment) |
| `**/api/orders/**` | `/api/orders/123/items` (recursive) |
| `**/api/**` | Anything under `/api/` |
| `https://api.stripe.com/**` | Only Stripe |

## HAR record / replay

### Record

```csharp
// Option 1: at context creation
var context = await Browser.NewContextAsync(new()
{
    RecordHarPath      = "orders.har",
    RecordHarUrlFilter = "**/api/**",
});
// ... navigate ...
await context.CloseAsync(); // HAR saved on close

// Option 2: programmatic
await Page.RouteFromHARAsync("orders.har", new()
{
    Update = true,
    Url    = "**/api/**",
});
```

### Replay (full mock)

```csharp
await Page.RouteFromHARAsync("orders.har", new()
{
    Url      = "**/api/**",
    NotFound = HarNotFound.Fallthrough, // unknown requests go to backend
    // NotFound = HarNotFound.Abort,    // unknown requests abort
});

await Page.GotoAsync("/orders");
```

When to use HAR: realistic responses from external APIs; "golden file" testing. **Avoid when Aspire stubs already exist** — stubs are more maintainable.

## WebSocket interception (1.46+)

```csharp
await Page.RouteWebSocketAsync("**/ws/notifications", ws =>
{
    ws.OnMessage(frame =>
    {
        if (frame.Text?.Contains("ping") == true)
            ws.Send("pong");
    });
});
```

## Wait for a specific response

`RunAndWaitFor` registers the listener BEFORE the action — order cannot be inverted.

```csharp
var response = await Page.RunAndWaitForResponseAsync(
    async () => await Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync(),
    resp => resp.Url.Contains("/api/orders") && resp.Status == 200);

var body = await response.JsonAsync();
Assert.IsNotNull(body);
```

## When NOT to use route interception

When the team already runs **Aspire stubs** (stateful processes alongside the AppHost), prefer those for:
- External APIs with state (Stripe, Twilio) — stub keeps state in memory.
- End-to-end flows where you want the full BFF — `RouteAsync` bypasses the server.

Use `RouteAsync` when:
- Simulating a network error the stub cannot produce (DNS failure, connection timeout).
- Quick smoke tests that don't need the full stack.
- Verifying the frontend's HTTP-500 handling.

## Sources

- https://playwright.dev/dotnet/docs/network
- https://playwright.dev/dotnet/docs/api/class-route
- https://playwright.dev/dotnet/docs/mock
