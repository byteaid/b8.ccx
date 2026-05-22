# Dependency Flow, Reference Matrix, End-to-End Example

Hexagonal architecture is enforced through what each project may reference. The application sees abstractions only; the host is the single composition root that meets concrete adapters.

## Diagram

```
                    ┌──────────────────────────────┐
                    │            Host              │
                    │   (WebAPI, Worker, gRPC, …)  │
                    │     composition root / DI    │
                    └──────────────┬───────────────┘
                                   │ calls
                                   ▼
                    ┌──────────────────────────────┐
                    │         Application          │
                    │      (Company.Product)       │
                    │   IProductService, …         │
                    │   ProductService, …          │
                    └──────┬───────────────┬───────┘
                  uses     │               │ depends on
                           ▼               ▼
       ┌──────────────────────────┐   ┌──────────────────────────────────────┐
       │  Company.Product.        │   │     Company.Product.Infrastructure   │
       │       Interface          │   │      (IRepository, ICache, …)        │
       │  (Commands · Results ·   │   └──────────────────▲───────────────────┘
       │   Events · Completions)  │                      │ implements
       └────────────┬─────────────┘                      │
                    │ depends on              ┌────────────────────────────┐
                    │                         │  Infrastructure adapters   │
                    │                         │ (SqlServer, AzureStorage,  │
                    │                         │           Redis, …)        │
                    │                         └─────────────┬──────────────┘
                    │                                       │ depends on
                    ▼                                       ▼
                 ┌──────────────────────────────────────────────┐
                 │             Models · Constants               │
                 │           (entities, enums)                  │
                 └──────────────────────────────────────────────┘
```

Application sits directly below Host. Below Application: `Interface` (left, the Host ↔ Application contract surface — Host also uses it) and `Company.Product.Infrastructure` (right, the abstractions Application depends on). The concrete `Infrastructure adapters` implement those abstractions; Host instantiates them at the composition root, so they hang off the right side rather than the Application path. `Models · Constants` is the shared substrate at the bottom that everyone consumes — including `Interface`, whose Commands/Results/Events carry domain types from `Models` and codes (`ErrorCode`, `ProductStatus`, …) from `Constants`.

## Reference matrix

Rows reference columns. ✓ allowed, ✗ forbidden, — N/A.

| ↓ from / → to            | Interface | Models | Constants | Infrastructure (abstractions) | Adapters | Application |
|--------------------------|:---------:|:------:|:---------:|:-----------------------------:|:--------:|:-----------:|
| Host                     | ✓         | ✓      | ✓         | ✓                             | ✓        | ✓           |
| Application (Core)       | ✓         | ✓      | ✓         | ✓                             | ✗        | —           |
| Adapters (Infra impls)   | ✗         | ✓      | ✓         | ✓                             | —        | ✗           |
| Infrastructure (abstr.)  | ✗         | ✓      | ✓         | —                             | ✗        | ✗           |
| Interface                | —         | ✓      | ✓         | ✗                             | ✗        | ✗           |

## Invariants

- **Infrastructure → Interface: forbidden.** Neither the abstractions project nor the adapters reference `Company.Product.Interface`. Commands/Results/Events live on the Host ↔ Application boundary only.
- **Application → Infrastructure adapters: forbidden.** The application never references a concrete adapter project (`Company.Product.SqlServer`, `Company.Product.Redis`, …). It only knows the abstractions in `Company.Product.Infrastructure`.
- **Adapters → Application: forbidden.** Adapters do not reach back into application services; they implement infrastructure ports and that is all.
- **Application ↔ Host: one-way.** Host references the application, not the other way around.
- **Composition lives in Host alone.** Swapping `Redis` for `Memcached` is a Host-only change; the application is untouched.
- **Interface → Infrastructure: forbidden.** Interface is data; it does not know about repositories, caches, or storage.

## PR review checklist

When reviewing a change, walk the matrix:

1. Open the touched `.csproj` files. Check `<ProjectReference>` lines against the matrix.
2. Check `using` directives in the new code. A `using Company.Product.SqlServer` inside `Company.Product` is a violation. A `using Company.Product.Interface` inside `Company.Product.SqlServer` is a violation.
3. Check namespace placement. `IProductRepository` under `Company.Product.Interface.*` is a violation (it belongs in `Company.Product.Infrastructure.*`).
4. Check that `*Event` types inherit from the shared `Event` base, not from `System.EventArgs`.
5. Check `ErrorCode` usage. New failure modes go in the existing enum, not in a sibling enum.

## End-to-end implementation example

A concrete walk-through tying every layer together. The example is "create an order" via HTTP.

### 1. Define the Command and Result types (Interface)

```csharp
// Company.Product.Interface/Commands/OrderCommands.cs
public class CreateOrderCommand : Command
{
    public string CustomerName { get; set; } = string.Empty;
    public List<OrderItem> Items { get; set; } = new();
}

// Company.Product.Interface/Completions/OperationCompletions.cs
public class OrderCreatedCompletion : Result
{
    public Guid OrderId { get; init; }
}
```

### 2. Define the Repository abstraction (Infrastructure abstractions)

```csharp
// Company.Product.Infrastructure
public interface IOrderRepository : IRepository<Order>
{
    Task<Guid> Add(Order order);
}
```

### 3. Implement the business logic (Core)

```csharp
// Company.Product
public interface IOrderService
{
    Task<Result> CreateOrder(CreateOrderCommand command);
}

public class OrderService : IOrderService
{
    private readonly IOrderRepository _repository;

    public OrderService(IOrderRepository repository) => _repository = repository;

    public async Task<Result> CreateOrder(CreateOrderCommand command)
    {
        if (command.Items.Count == 0)
            return new FailedResult { CommandId = command.CommandId, Code = ErrorCode.ValidationFailed };

        var order = new Order
        {
            OrderId = Guid.NewGuid(),
            CustomerName = command.CustomerName,
            Items = command.Items,
            CreatedAt = DateTime.UtcNow
        };

        await _repository.Add(order);

        return new OrderCreatedCompletion
        {
            CommandId = command.CommandId,
            OrderId = order.OrderId
        };
    }
}
```

### 4. Implement the Repository (Adapter)

```csharp
// Company.Product.SqlServer
public class OrderRepository : IOrderRepository
{
    private readonly ApplicationDbContext _context;
    private readonly IOrderMapper _mapper;

    public OrderRepository(ApplicationDbContext context, IOrderMapper mapper)
    {
        _context = context;
        _mapper = mapper;
    }

    public async Task<Guid> Add(Order order)
    {
        OrderEntity entity = _mapper.ToEntity(order);
        _context.Orders.Add(entity);
        await _context.SaveChangesAsync();
        return entity.OrderId;
    }
}
```

### 5. Expose the endpoint (Host)

```csharp
// Company.Product.WebAPI
[HttpPost("orders")]
public async Task<ActionResult> CreateOrder([FromBody] CreateOrderCommand command)
{
    Result result = await _orderService.CreateOrder(command);

    return result switch
    {
        OrderCreatedCompletion ok => Ok(new { ok.OrderId }),
        FailedResult { Code: ErrorCode.ValidationFailed } => BadRequest(result),
        FailedResult => StatusCode(500, result),
        _ => StatusCode(500)
    };
}
```

### 6. Test it end-to-end

A `HTTP/Orders_Tests.cs` class under `Company.Product.Test` mounts the AppHost in `[ClassInitialize]`, drives the endpoint via `app.CreateHttpClient("webapi")`, and asserts on the HTTP response. Layout, MSTest mechanics, and seeding belong to the `dotnet-testing` skill.

## Cross-references

- [skill.md](skill.md) — non-negotiable rules including rule 11 (test layout).
- [solution-layout.md](solution-layout.md) — project references that mirror this matrix.
- [interface-and-bases.md](interface-and-bases.md) — Commands/Results/Events placement.
- [core-and-infrastructure.md](core-and-infrastructure.md) — service, abstraction, adapter, host shapes.
- `dotnet-aspire` — AppHost wiring + integration-test harness.
