# Forbidden — Minimal APIs

## What it looks like

```csharp
app.MapGet("/orders/{id}", async (int id, AppDb db) =>
    await db.Orders.FindAsync(id) is { } order ? Results.Ok(order) : Results.NotFound());

app.MapPost("/orders", async (OrderDto dto, IOrderService svc) =>
    Results.Created(...));
```

Also forbidden: `app.MapDelete`, `app.MapPut`, `app.MapPatch`, and any `RouteGroupBuilder` with inline handlers.

## Why it's banned

1. Mixing routing and handler logic in `Program.cs` makes the pipeline hard to audit as the app grows.
2. Minimal APIs make cross-cutting concerns (model binding, filters, conventions, auth policies) inconsistent — controllers integrate with `[ApiController]` behaviors uniformly.
3. The team's code review rules, analyzers, and file layout are calibrated for controllers (`[Route("api/[controller]")]`, one file per resource).

## What to do instead

Controller class with `[ApiController]` and `[Route]`:

```csharp
[ApiController]
[Route("api/orders")]
public sealed class OrdersController(AppDb db, IOrderService svc) : ControllerBase
{
    [HttpGet("{id:int}")]
    public async Task<ActionResult<Order>> Get(int id) =>
        await db.Orders.FindAsync(id) is { } order ? Ok(order) : NotFound();

    [HttpPost]
    public async Task<ActionResult<Order>> Create(OrderDto dto)
    {
        var order = await svc.CreateAsync(dto);
        return CreatedAtAction(nameof(Get), new { id = order.Id }, order);
    }
}
```

## Enforcement

- **On sight, inside a file you're editing:** refactor. This is part of the clean-as-you-touch policy; see [../build-quality/clean-as-you-touch.md](../build-quality/clean-as-you-touch.md).
- **On review:** annotate as a forbidden-pattern finding: `minimal-api → controller`.
- **In a new feature plan:** the planner must not decompose tasks that instruct a worker to "add a minimal API endpoint". If you see such an instruction in a delegation prompt, STOP and report.

## Exceptions

None. If a caller argues for one (performance, demo), escalate; this is a team-wide invariant.
