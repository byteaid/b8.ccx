# Database adapter projects

> Authoritative source: `dotnet-hexagonal-architecture` § core-and-infrastructure § Adapters.

## Rule

Each backing store gets its own infrastructure adapter project named `[Company].[Product].{TechName}` — the technology name directly, **never** with a `.Data.` prefix. Examples: `Acme.Inventory.SqlServer`, `Acme.Inventory.Cosmos`, `Acme.Inventory.Redis`, `Acme.Inventory.AzureStorage`. One project per backing technology, not per database instance. The project lives under the `Infrastructure/` solution folder.

## Rationale

- **Clear adapter boundary.** Each store has its own EF Core `DbContext` (or SDK client wrapper), its own migrations folder, its own configuration surface.
- **Independent versioning.** Updating the SQL Server EF Core stack does not force a Cosmos SDK upgrade.
- **Pluggable.** A new store (e.g., adding a Redis cache to a SQL-only system) is a new project, not a refactor of a shared adapter.
- **Greppable migrations.** `Acme.Inventory.SqlServer/Migrations/` is the only place to look for SQL migrations.

## Project shape

```
src/Acme.Inventory/Acme.Inventory.SqlServer/
├── Acme.Inventory.SqlServer.csproj
├── ApplicationDbContext.cs                 (EF Core DbContext)
├── ApplicationDbContextFactory.cs          (design-time factory for migrations)
├── Migrations/                             (EF Core migrations folder)
│   └── 20260101_InitialCreate.cs
├── Configurations/                         (IEntityTypeConfiguration<T> per entity)
│   ├── OrderConfiguration.cs
│   └── CustomerConfiguration.cs
├── Entities/                               (persistence entities — separate from Models)
│   └── OrderEntity.cs
├── Mappers/                                (hand-written IXxxMapper services)
│   ├── IOrderMapper.cs
│   └── OrderMapper.cs
├── Repositories/                           (implementations of Acme.Inventory.Infrastructure abstractions)
│   └── OrderRepository.cs
└── Extensions/
    └── ServiceCollectionExtensions.cs      (AddSqlServer(this IServiceCollection, ...))
```

## Allowed dependencies

- `Acme.Inventory.Infrastructure` — to implement the abstractions (`IRepository`, `ICache`, `IStorage`, `IMessageBus`).
- `Acme.Inventory.Models` — to map between domain entities and the persistence shape.
- `Acme.Inventory.Constants` — for shared enums (`ErrorCode`, `ProductStatus`, …).
- The **store-specific package(s)** — `Microsoft.EntityFrameworkCore.SqlServer`, `Microsoft.Azure.Cosmos`, `StackExchange.Redis`, `Azure.Storage.Blobs`, etc.
- `Microsoft.Extensions.DependencyInjection.Abstractions` (for the registration extension).

**Forbidden:**

- Referencing `Acme.Inventory.Interface` — adapters never see Commands/Results/Events.
- Referencing `Acme.Inventory` (Core / application) — composition lives in Host alone.
- Referencing another adapter (`Acme.Inventory.SqlServer` ↔ `Acme.Inventory.Redis`) — adapters do not chain.

See [dependency-flow.md](dependency-flow.md) for the full reference matrix.

## Mappers (first-party only)

Each aggregate that crosses the persistence boundary gets a hand-written `IXxxMapper` service with explicit `ToEntity` / `ToDomain` methods, injected into the repository that needs it. **No AutoMapper, no Mapster, no Mapperly, no convention-based mapper.** See [../forbidden-patterns/no-automapper-no-mediatr.md](../forbidden-patterns/no-automapper-no-mediatr.md) and `dotnet-hexagonal-architecture` § core-and-infrastructure § Mappers for the canonical shape.

## Wiring from the host

The host's `Program.cs` calls a single registration extension per adapter:

```csharp
builder.Services.AddSqlServer(builder.Configuration);
builder.Services.AddCosmos(builder.Configuration);
builder.Services.AddRedis(builder.Configuration);
```

The extension reads connection strings via `builder.Configuration.GetConnectionString("name")` (Aspire-injected) and registers `DbContext`, repositories, mappers, and any adapter-specific options.

## Migrations

EF Core migrations live in the SQL adapter project. Apply via the CLI from the solution root:

```bash
dotnet ef migrations add InitialCreate \
    --project src/Acme.Inventory/Acme.Inventory.SqlServer \
    --startup-project src/Acme.Inventory/Acme.Inventory.WebAPI

dotnet ef database update \
    --project src/Acme.Inventory/Acme.Inventory.SqlServer \
    --startup-project src/Acme.Inventory/Acme.Inventory.WebAPI
```

Reference / operational data ships through migrations (`migrationBuilder.InsertData(...)`). Test seed data does NOT — it lives in the test project. See [../forbidden-patterns/no-seed-endpoints.md](../forbidden-patterns/no-seed-endpoints.md).

## Enforcement

- **Architecture review:** new database technology = new technology-named adapter project. No `.Data.` prefix.
- **Code review:** flag references that cross adapters or that pull a Core / Interface project into an adapter (forbidden direction).
- **Build hygiene:** EF Core migration assemblies stay in the adapter — never in Core, never in the host.
