# Application, Infrastructure Abstractions, Adapters, Hosts

Where the work actually happens. The application project (`Company.Product`) holds business logic and the service interfaces it exposes to hosts. `Company.Product.Infrastructure` declares the abstractions the application depends on. Adapters under the `Infrastructure/` solution folder implement those abstractions against a specific technology. Hosts compose the graph at the entry point.

## 1. Application — `Company.Product`

The application owns:

- Service interfaces (`IProductService`, `IOrderService`) — the application defines its own contracts.
- Service implementations.
- Pure business logic, validation, and orchestration.
- Event declarations (the `event` keyword on services), bound to delegates whose types live in `.Interface/Events/`.

```csharp
public interface IProductService
{
    Task<Result> CreateProduct(CreateProductCommand command);
    Task<Result> UpdateProduct(UpdateProductCommand command);
}

public class ProductService : IProductService
{
    private readonly ILogger<ProductService> _logger;
    private readonly IProductRepository _products;
    private readonly ICategoryRepository _categories;

    public ProductService(ILogger<ProductService> logger,
        IProductRepository products, ICategoryRepository categories)
    {
        _logger = logger;
        _products = products;
        _categories = categories;
    }

    public async Task<Result> CreateProduct(CreateProductCommand command)
    {
        // Pure business logic — no SQL, no HTTP, no Azure SDK.
    }
}
```

The application only knows abstractions; it never references a concrete adapter project (`.SqlServer`, `.Redis`).

## 2. Infrastructure abstractions — `Company.Product.Infrastructure`

Inner-ring project that declares the ports the application calls into. No concrete tech (no `Microsoft.Data.SqlClient`, no `Azure.Storage.Blobs`, no `StackExchange.Redis` references).

```csharp
public interface IRepository<T> where T : class
{
    Task Add(T entity);
    Task Update(T entity);
    Task<T?> GetById(Guid id);
}

public interface IProductRepository : IRepository<Product>
{
    Task<IReadOnlyList<Product>> SearchByCategory(Guid categoryId);
}

public interface ICache
{
    Task<T?> Get<T>(string key);
    Task Set<T>(string key, T value, TimeSpan? ttl = null);
}

public interface IStorage
{
    Task<Uri> Upload(string container, string name, Stream content);
    Task<Stream> Download(string container, string name);
}
```

References allowed: `Company.Product.Models`, `Company.Product.Constants`. Nothing else.

## 3. Adapters — `Company.Product.SqlServer` / `.AzureStorage` / `.Redis` / …

Concrete implementations of the abstractions. Named after the technology (`Company.Product.SqlServer`, never `.Data.SqlServer`). Live in the top-level `Infrastructure/` solution folder; physically flat in `src/Company.Product/`.

Internal structure (typical SQL Server adapter):

```
Company.Product.SqlServer/
├── Entities/
│   └── ProductEntity.cs
├── Repositories/
│   └── ProductRepository.cs
├── Mappers/
│   ├── IProductMapper.cs
│   └── ProductMapper.cs
├── Migrations/
└── ApplicationDbContext.cs
```

Adapters reference `Company.Product.Infrastructure` (for the abstractions) plus `.Models` and `.Constants` (for domain types and codes). They **never** reference `.Interface` and **never** reference the application project.

### Mappers — placement only

The adapter project owns its mappers (one `IXxxMapper` per aggregate that crosses the persistence boundary, injected into the repository that needs it). The rule itself — "no AutoMapper / Mapster / MediatR / Brighter; hand-written first-party `IXxxMapper`" — and the canonical mapper / repository templates live in `dotnet-conventions` § forbidden-patterns/no-automapper-no-mediatr. Do not restate them here.

## 4. Hosts — inbound adapters

Hosts are the only place that references **both** the application project and concrete adapters; they own the composition root.

A Host is **any executable adapter that drives the application from the outside**. Transport doesn't matter — HTTP, gRPC, message queue, terminal, GUI, push notifications. What makes a project a Host:

- It owns a process entry point (`Program.cs` / `Main`).
- It performs DI composition — registers application services and binds the chosen infrastructure adapters.
- It translates an external trigger into a Command sent to an application service.
- It maps `Result` subtypes to its surface's response shape (HTTP status, CLI exit code, UI state, gRPC status).

Common host shapes and how they bind:

| Host | Triggered by | Translates into | Maps Result via |
|---|---|---|---|
| `.WebAPI` | HTTP request | `[HttpPost]` action body bound to a `*Command` | `ActionResult` / status code switch on the `Result` subtype |
| `.gRPC.Server` | gRPC call | proto message → `*Command` (mapper or hand-written) | gRPC `Status` codes |
| `.Worker` | hosted service tick / queue message | message body → `*Command` | logs + retry policy |
| `.Cli` | parsed CLI invocation | `parseResult.GetValue(opt)` → `*Command` | exit code |
| `.Web` (Blazor) / `.Mobile` (MAUI) / `.Desktop` (WPF) | UI event | bound form → `*Command` | UI state transition (toast, navigation) |

DI registration sits in `Program.cs`:

```csharp
builder.Services.AddScoped<IProductRepository, ProductRepository>();   // adapter binding
builder.Services.AddScoped<IProductMapper, ProductMapper>();           // first-party mapper
builder.Services.AddScoped<IProductService, ProductService>();         // application service
builder.Services.AddScoped<ICache, RedisCache>();
```

Swapping `Redis` for `Memcached` is a Host-only change: replace the `ICache` registration; the application is untouched.

### Aspire AppHost

Every product also ships a `Company.Product.AppHost` under `Host/`. It declares each host as an Aspire resource, wires resources from `Infrastructure/`, and serves as the canonical entry for `dotnet run` (local dev) and the integration tests. It is the **single** orchestration project; production and tests share it. The mode (real infra vs emulators/stubs) is a configuration switch read at the top of `Program.cs`. Mechanics live in the `dotnet-aspire` skill.

## 5. Events — delegate-first, raised only from application services

Events are intermediate notifications surfaced **while a command is being processed** (progress signals, partial results, observable side-effects). They are **raised only from application services** — no other layer raises them.

Conventions:

- Event type name ends in **`Event`** (not `EventArgs`). Derives from the shared `Event` base. **Does not** inherit from `System.EventArgs`.
- The delegate signature still uses the C# `(object? sender, TEvent e)` shape.
- Declare the type and its handler delegate in `Company.Product.Interface/Events/`.
- The application service exposes them with the `event` keyword and raises them at the appropriate points.
- **Default to plain C# delegate events.** No bus, no mediator, no third-party library until a concrete need appears (cross-process delivery, persistence, broad fan-out across decoupled modules).

```csharp
// Company.Product.Interface/Events/ProductEvents.cs
public class ProductValidationStartedEvent : Event { }

public class ProductPriceAdjustedEvent : Event
{
    public decimal OriginalPrice { get; init; }
    public decimal AdjustedPrice { get; init; }
    public string Reason { get; init; } = string.Empty;
}

public delegate void ProductValidationStartedHandler(
    object? sender, ProductValidationStartedEvent e);

public delegate void ProductPriceAdjustedHandler(
    object? sender, ProductPriceAdjustedEvent e);
```

```csharp
// Company.Product/ProductService.cs
public class ProductService : IProductService
{
    public event ProductValidationStartedHandler? ProductValidationStarted;
    public event ProductPriceAdjustedHandler? ProductPriceAdjusted;

    public async Task<Result> CreateProduct(CreateProductCommand command)
    {
        ProductValidationStarted?.Invoke(this, new ProductValidationStartedEvent
        {
            CommandId = command.CommandId
        });

        // ... validation, price adjustment, persistence ...
    }
}
```

Subscribers live in the Host — typically subscribed during DI composition or as a hosted service:

```csharp
// Company.Product.WebAPI/Program.cs (subscriber side)
productService.ProductValidationStarted += (_, e) =>
    logger.LogInformation("Validation started for {CommandId} at {At}",
        e.CommandId, e.At);
```

## Cross-references

- [solution-layout.md](solution-layout.md) — physical placement of the projects discussed here.
- [interface-and-bases.md](interface-and-bases.md) — Commands, Results, Events declared in `.Interface`.
- [dependency-flow.md](dependency-flow.md) — what may reference what.
- `dotnet-aspire` — how the AppHost wires hosts and adapters at orchestration time.
