# `LoggerMessage` — high-performance logging

## Rule

Declare every log statement as a `[LoggerMessage]`-attributed `partial` method. Never call `logger.LogInformation("...{X}...", x)` with interpolation or run-time formatted strings in performance-sensitive paths (controllers, hubs, hot loops, hosted services).

## Rationale

- **Compile-time template parsing** — placeholders are validated at build; mismatches between template and arguments fail the build.
- **Zero boxing for value-type arguments** — generated overload takes typed parameters directly.
- **Source-checked event IDs** — duplicate IDs across the app surface as analyzer warnings.
- **AOT-friendly** — no reflection, no `string.Format` at runtime.
- **Allocation-free fast path** — when the level is disabled, the method short-circuits without building the message.

## Canonical shape

```csharp
public sealed partial class OrdersController(
    ILogger<OrdersController> logger,
    IOrderService svc) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(OrderDto dto, CancellationToken ct)
    {
        Log.OrderCreating(logger, dto.CustomerId);
        var result = await svc.CreateAsync(dto, ct);
        if (result.IsError)
        {
            Log.OrderCreateFailed(logger, dto.CustomerId, result.FirstError.Code);
            return BadRequest(result.Errors);
        }
        Log.OrderCreated(logger, result.Value.Id);
        return CreatedAtAction(nameof(Get), new { id = result.Value.Id }, result.Value);
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 1001, Level = LogLevel.Information,
            Message = "Creating order for customer {CustomerId}")]
        public static partial void OrderCreating(ILogger logger, Guid customerId);

        [LoggerMessage(EventId = 1002, Level = LogLevel.Information,
            Message = "Order {OrderId} created")]
        public static partial void OrderCreated(ILogger logger, Guid orderId);

        [LoggerMessage(EventId = 1003, Level = LogLevel.Warning,
            Message = "Failed to create order for {CustomerId}: {ErrorCode}")]
        public static partial void OrderCreateFailed(ILogger logger, Guid customerId, string errorCode);
    }
}
```

## Conventions

- `Log` is a **nested partial class** of the consuming type, `private static`. Keeps message definitions co-located with their use site.
- **Event IDs are unique within the app.** Pick a per-area block (`1000-1099` for orders, `2000-2099` for payments…) and document the allocation in `docs/ARCHITECTURE.md` or a dedicated `LOG_EVENTS.md`.
- **Levels match severity.** `Information` for business-meaningful events, `Warning` for recoverable problems, `Error` for failures, `Debug`/`Trace` for diagnostics.
- **No exception interpolation** — pass the exception as the second argument to the generated method (`Log.OrderFailed(logger, ex, customerId)`).

## When inline logging is acceptable

Only outside hot paths and for one-off diagnostics: a `Program.cs` startup banner, a tool's `Console.WriteLine`-equivalent. The cost-benefit of `LoggerMessage` is most tangible in request handlers and worker loops.

## Enforcement

- **Code review:** flag `logger.LogXxx($"...{x}...")` — interpolated strings in `LogXxx` skip the level check and allocate even when the level is disabled.
- **Analyzer:** enable `CA1848` (use `LoggerMessage` for performance) and treat as a warning that becomes an error under `-warnaserror`.
