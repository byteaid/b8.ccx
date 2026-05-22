# `TimeProvider` for date/time

## Rule

Inject `TimeProvider` and call `timeProvider.GetUtcNow()` whenever code needs the current time. Never call `DateTime.UtcNow`, `DateTime.Now`, `DateTimeOffset.UtcNow`, `DateTimeOffset.Now`, or `Stopwatch.GetTimestamp()` directly from production code.

## Rationale

- Tests inject a `FakeTimeProvider` to drive deterministic time without bypass code in the app. The app stays untouched; the test owns the clock.
- One source of truth for "now". Centralized at the DI boundary — no scattered ambient calls.
- `TimeProvider` carries timer creation too (`CreateTimer`), so periodic background work also remains testable.
- This is a non-test team rule: production code that calls `DateTime.UtcNow` directly is a bug, even if "tests don't depend on it yet" — the next test that does has no clean way to control time.

## Canonical shape

```csharp
public sealed class OrdersController(AppDb db, TimeProvider time) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<Order>> Create(OrderDto dto, CancellationToken ct)
    {
        var order = new Order
        {
            Id = Guid.CreateVersion7(),
            CreatedAt = time.GetUtcNow(),
            // ...
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = order.Id }, order);
    }
}
```

DI registration in host `Program.cs`:

```csharp
builder.Services.AddSingleton(TimeProvider.System);
```

In tests:

```csharp
var fake = new FakeTimeProvider(startsAt: DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
// register `fake` as TimeProvider in the test fixture's seam, then advance:
fake.Advance(TimeSpan.FromMinutes(5));
```

## Enforcement

- **Banned literals:** `DateTime.UtcNow`, `DateTime.Now`, `DateTimeOffset.UtcNow`, `DateTimeOffset.Now`. See [../forbidden-patterns/no-datetime-utcnow.md](../forbidden-patterns/no-datetime-utcnow.md).
- **Clean-as-you-touch:** swap in the same pass when the file is already open.
- **Code review:** flag any direct ambient time call.
