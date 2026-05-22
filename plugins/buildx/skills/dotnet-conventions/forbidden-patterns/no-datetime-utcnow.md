# Forbidden — `DateTime.UtcNow` and friends

## What it looks like

```csharp
// Anywhere in production code
var now = DateTime.UtcNow;
var nowLocal = DateTime.Now;
var nowOff = DateTimeOffset.UtcNow;
var stamp = DateTimeOffset.Now;
var elapsed = Stopwatch.GetTimestamp();
order.CreatedAt = DateTime.UtcNow;
```

## Why it's banned

1. **Untestable.** Tests cannot freeze, advance, or compare against ambient calls. Adding a "test mode" to swap the clock pollutes production code (see [no-test-specific-branches.md](no-test-specific-branches.md)).
2. **No single source of truth.** Different files end up calling different ambient APIs (`DateTime` vs `DateTimeOffset`, UTC vs local), producing time skew across the system.
3. **`TimeProvider` (built into .NET 8+) solves this cleanly.** Inject `TimeProvider`, call `GetUtcNow()`, and the test passes a `FakeTimeProvider` to drive deterministic time.
4. **Team rule, no exceptions.** Even a single ambient `DateTime.UtcNow` weakens the discipline of every other module that needs determinism.

## What to do instead

```csharp
public sealed class OrderService(AppDb db, TimeProvider time) : IOrderService
{
    public async Task<Order> CreateAsync(OrderDto dto, CancellationToken ct)
    {
        var order = new Order
        {
            Id = Guid.CreateVersion7(),
            CustomerId = dto.CustomerId,
            CreatedAt = time.GetUtcNow(),     // injected
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);
        return order;
    }
}

// Program.cs
builder.Services.AddSingleton(TimeProvider.System);
```

Tests inject `FakeTimeProvider`:

```csharp
var fake = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
// register fake instead of TimeProvider.System in the test fixture's seam.
fake.Advance(TimeSpan.FromMinutes(5));
```

## What about timers?

`TimeProvider.CreateTimer(...)` replaces `System.Threading.Timer` and `Task.Delay` for testable periodic work. Background services should accept `TimeProvider` and use it for scheduling.

## Enforcement

- **On sight, inside a file you're editing:** swap the ambient call for `timeProvider.GetUtcNow()` and inject `TimeProvider` if the class doesn't already take it. See [../build-quality/clean-as-you-touch.md](../build-quality/clean-as-you-touch.md).
- **Quick scan:**

  ```bash
  grep -rE "DateTime\.(UtcNow|Now)|DateTimeOffset\.(UtcNow|Now)" src/ \
    | grep -v "test/.*\.Test/"
  ```

  must return no matches outside the test project.

## Carve-outs

- **Tests** may use ambient time for assertions ("the response was sent at roughly `DateTime.UtcNow`"); production code may not.
- **`Stopwatch`** for measuring durations is fine — that is not "current time", it's elapsed time.

## See also

- [../csharp-style/time-provider.md](../csharp-style/time-provider.md) — the positive rule and canonical shape.
