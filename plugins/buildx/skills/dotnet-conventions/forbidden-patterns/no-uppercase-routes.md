# Forbidden — Uppercase characters in route templates

## What it looks like

```csharp
[Route("api/Orders")]               // PascalCase segment
public sealed class OrdersController : ControllerBase
{
    [HttpGet("Active")]              // PascalCase action segment
    public ActionResult<IReadOnlyList<Order>> GetActive() => ...;

    [HttpGet("by-Customer/{id}")]    // mixed case
    public ActionResult<...> ByCustomer(Guid id) => ...;
}

// SignalR
app.MapHub<NotificationsHub>("/Hubs/Notifications");
```

## Why it's banned

1. **HTTP path matching is case-sensitive on the wire.** `GET /api/Orders` and `GET /api/orders` are different routes; mismatched cases break clients.
2. **Inconsistent casing across the codebase** is a permanent foot-gun for new contributors who copy a sibling pattern that happens to be wrong.
3. **OpenAPI / Swagger output reflects the route exactly** — uppercase paths leak into generated client SDKs and developer documentation.
4. **`RouteOptions.LowercaseUrls = true` only normalizes the controller-derived segment** (`[controller]`); hardcoded segments still have to be lowercase by hand.

## What to do instead

All routes lowercase. Configure the option globally and write hardcoded segments lowercase:

```csharp
// Program.cs
builder.Services.Configure<RouteOptions>(options =>
{
    options.LowercaseUrls = true;
    options.LowercaseQueryStrings = true;
});

// Controllers
[Route("api/orders")]
public sealed class OrdersController : ControllerBase
{
    [HttpGet("active")]
    public ActionResult<IReadOnlyList<Order>> GetActive() => ...;

    [HttpGet("by-customer/{id:guid}")]
    public ActionResult<Order> ByCustomer(Guid id) => ...;
}

// SignalR — paths under /hubs/*
app.MapHub<NotificationsHub>("/hubs/notifications");
```

Conventions:

- `/api/*` — REST endpoints (controllers).
- `/hubs/*` — SignalR.
- `/_framework/*` — reserved for Blazor WASM runtime; never define a route there.
- Multi-word path segments use kebab-case (`by-customer`, not `byCustomer` or `BySupplier`).

## Enforcement

- **On sight, inside a file you're editing:** lowercase the route template and confirm `RouteOptions.LowercaseUrls = true` is set in `Program.cs`. See [../build-quality/clean-as-you-touch.md](../build-quality/clean-as-you-touch.md).
- **On review:** any uppercase character in a route attribute string is a blocking finding.
- **Quick scan:**

  ```bash
  grep -rE "\[(Route|Http(Get|Post|Put|Delete|Patch))\(\"[^\"]*[A-Z]" src/
  ```

  should return zero matches in controllers.
