# `sealed` by default

## Rule

Every concrete class is `sealed` unless inheritance is the explicit design intent. Apply to controllers, services, repositories, DI-registered types, value objects, DTOs, options classes, hosted services, hubs, gRPC service implementations, fixtures.

## Rationale

- Sealed = no virtual dispatch, devirtualization, smaller IL, faster JIT.
- Sealed = no surprise overrides; behavior is fixed at the leaf.
- Sealed signals intent: "this class is a leaf of the type hierarchy." Unsealing later is reversible; sealing later is breaking.
- Inheritance is rarely the right tool — composition, interfaces, and source-generated patterns cover almost every case.

## Canonical shape

```csharp
public sealed class OrdersController(AppDb db, IOrderService svc) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Order>> Get(Guid id) =>
        await db.Orders.FindAsync(id) is { } order ? Ok(order) : NotFound();
}

public sealed record OrderDto(Guid Id, string CustomerId, decimal Total);

public sealed class FakeTimeProviderHostedService(TimeProvider time) : BackgroundService { /* ... */ }
```

## When NOT sealed

Open the class only if at least one of these holds:

- It is an **abstract base** (`abstract class`) by design.
- A test fixture or framework genuinely needs to subclass it (rare — favor composition or interfaces first).
- It is a **public API surface** of a library that wants extensibility (does not apply to internal application code).

If you unseal, leave a one-line comment explaining why.

## Enforcement

- **Code review:** flag any non-sealed `class` without a justifying comment.
- **Clean-as-you-touch:** if you open a file with non-sealed leaf classes, seal them in the same pass. See [../build-quality/clean-as-you-touch.md](../build-quality/clean-as-you-touch.md).
- **Build:** consider enabling the analyzer rule that warns on unsealed internal classes (`CA1852`). Do not suppress — fix.
