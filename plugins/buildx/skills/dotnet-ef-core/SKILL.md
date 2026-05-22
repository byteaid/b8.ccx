---
name: dotnet-ef-core
description: Authoring reference for Entity Framework Core 10 on .NET 10 in Aspire-orchestrated solutions. Covers DbContext registration with AddDbContextPool/AddDbContextFactory using standard SqlClient (NOT Aspire client integrations), Aspire-compatible IDesignTimeDbContextFactory, dotnet ef migrations add/update/script/bundle, idempotent seeding via migrationBuilder.InsertData, value converters with ValueComparer, JSON columns and owned types, TPH/TPT/TPC inheritance, AsSplitQuery, EF.CompileAsyncQuery, AsNoTracking, ExecuteUpdateAsync/ExecuteDeleteAsync, SaveChangesInterceptor for audit/soft-delete/domain events, EnableRetryOnFailure resiliency, and consumer-side TLS via SqlConnectionStringBuilder.TrustServerCertificate for containerized SQL Server.
when_to_use: |
  - Trigger keywords: EF Core, DbContext, AddDbContextPool, AddDbContextFactory, dotnet ef migrations, IDesignTimeDbContextFactory, ValueConverter, ValueComparer, OwnsOne, OwnsMany, ToJson, AsSplitQuery, EF.CompileAsyncQuery, AsNoTracking, ExecuteUpdateAsync, ExecuteDeleteAsync, SaveChangesInterceptor, EnableRetryOnFailure, TrustServerCertificate, HasQueryFilter, HasDiscriminator, UseTpcMappingStrategy.
  - Task shapes: scaffold a `[Company].[Product].{TechName}` adapter project, add/apply/script/bundle a migration, design value converters or JSON columns or owned types, choose between TPH/TPT/TPC, write a streaming/compiled/split query, set up audit/soft-delete/domain-events via interceptor, configure resiliency, fix containerized SQL Server TLS errors on the consumer.
allowed-tools: Bash, Edit, Glob, Grep, Monitor, NotebookEdit, PowerShell, Read, Write
user-invocable: false
paths: ["**/*Context.cs", "**/*DbContext.cs", "**/Migrations/*.cs", "**/*.SqlServer.csproj", "**/*.Cosmos.csproj", "**/*DesignTimeFactory.cs"]
---

# Entity Framework Core 10 — Team Skill

L1 dispatcher. Concrete content lives in L2 sub-files. Verify EF Core version with `dotnet ef --version` (target: 10.0.x). Most rules apply to 9.x; `AddDbContextPool` defaults and `ExecuteUpdateAsync` assume 10.

## Mental model

EF Core 10 is the team's data-access stack on top of SQL Server (containerized in dev via Aspire, managed instances in prod). Two design positions diverge from the Microsoft default:

- **Standard SqlClient only.** Consumers register `DbContext` with `services.AddDbContextPool<T>(opts => opts.UseSqlServer(...))`. Aspire **client** integrations (`Aspire.Microsoft.EntityFrameworkCore.SqlServer`, `builder.AddSqlServerDbContext<T>(...)`) are **not** used — the AppHost emits `ConnectionStrings__<name>` and the consumer reads it directly. This keeps the consumer portable and makes design-time-factory wiring straightforward.
- **TLS handshake fix lives on the consumer, not on the AppHost.** Containerized SQL Server (`mcr.microsoft.com/mssql/server:2022-latest`) signs its server cert with a self-signed CA. The fix is `TrustServerCertificate=true` applied via `SqlConnectionStringBuilder` in the consumer's `Program.cs` — never in `appsettings.json`, never on the AppHost side.

Aspire AppHost wiring (the `Add*` parent verb that produces the connection string) is out of scope here — see `dotnet-aspire`. Layer placement of the `[Company].[Product].{TechName}` adapter project (e.g. `Acme.Billing.SqlServer`) belongs to `dotnet-conventions`.

## Non-negotiable rules (must survive compaction)

1. **Standard clients always.** `services.AddDbContextPool<T>(opts => opts.UseSqlServer(...))`, NEVER `builder.AddSqlServerDbContext<T>(...)`. Read the CS via `Configuration.GetConnectionString("<name>")` where `<name>` matches the AppHost's `AddDatabase("<name>")` (NOT `AddSqlServer("<server>")`).
2. **`TrustServerCertificate=true` on the consumer.** Mandatory for containerized SQL Server. Apply via `SqlConnectionStringBuilder` in `Program.cs`. The AppHost does not add it; `appsettings.json` does not carry it.
3. **`AddDbContextPool` by default**, pool size 1024 in production. Switch to `AddDbContextFactory<T>` for Blazor Server and batch workers. Switch to plain `AddDbContext` only when the context injects scoped services (e.g. `ICurrentUser`) — pooling reuses the instance and would leak cross-request state.
4. **Migrations target the consumer (Web API / Worker), not the AppHost.** The `IDesignTimeDbContextFactory<T>` lives in the `[Company].[Product].{TechName}` adapter project (e.g. `Acme.Billing.SqlServer`) and resolves a connection string without Aspire running. See [migrations-seeding](migrations-seeding.md).
5. **Idempotent seeding, identical in dev and prod.** Use `migrationBuilder.InsertData` inside a migration, or a hand-written seeder that upserts by natural key. Never gate on `IsDevelopment()`. Dev-only fixtures live in a dedicated seeding type / data file inside the test project's `Seeding/` folder, never gated on `IsDevelopment()` in the production seeder.
6. **Soft-delete and audit via `SaveChangesInterceptor`, never via `SaveChanges()` override.** Interceptors compose; overrides do not. Use `TimeProvider`, not `DateTime.UtcNow`. Use `Guid.CreateVersion7()` for new IDs (sortable, index-friendly).
7. **`AsSplitQuery()` is mandatory when a query has 2+ collection `Include`s.** Cartesian explosion is silent in dev and pathological in prod. Configure globally with `UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)` if the codebase pattern is consistent.
8. **`AsNoTracking()` by default on read queries.** Tracking is reserved for the read-modify-save path. Use `AsNoTrackingWithIdentityResolution()` when the same row appears multiple times in a graph.
9. **Never `.Result` / `.Wait()`.** Async all the way: `ToListAsync`, `FirstOrDefaultAsync`, `SaveChangesAsync`.
10. **`EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null)` on every `UseSqlServer`.** With retry on, user-initiated transactions require `db.Database.CreateExecutionStrategy().ExecuteAsync(...)`. Bulk operations (`ExecuteUpdateAsync` / `ExecuteDeleteAsync`) bypass `SaveChanges` interceptors — accept the limitation explicitly.

## DbContext registration (canonical)

```csharp
// src/Acme.Billing.WebAPI/Program.cs
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

var rawCs = builder.Configuration.GetConnectionString("billingdb")
    ?? throw new InvalidOperationException("Connection string 'billingdb' not provided by the AppHost.");

var csBuilder = new SqlConnectionStringBuilder(rawCs)
{
    TrustServerCertificate = true,   // mandatory for containerized SQL Server
    Encrypt = true,
    CommandTimeout = 30,
};

builder.Services.AddDbContextPool<BillingDb>(opts =>
{
    opts.UseSqlServer(csBuilder.ConnectionString, sql =>
    {
        sql.MigrationsAssembly(typeof(BillingDb).Assembly.FullName);
        sql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null);
        sql.CommandTimeout(30);
    });
    opts.UseSnakeCaseNamingConvention();   // optional, via EFCore.NamingConventions
}, poolSize: 1024);
```

### Pool vs Factory vs plain

| Variant | When |
|---|---|
| `AddDbContextPool<T>` | Default for stateless request handlers (Web API, gRPC). |
| `AddDbContextFactory<T>` | Blazor Server components living past a request; batch workers needing a fresh context per message. Inject `IDbContextFactory<T>` and `await using var db = await factory.CreateDbContextAsync(ct);`. |
| `AddDbContext<T>` | DbContext injects a scoped service (e.g. `ICurrentUser`). Pooling would reuse the original-scope service and leak across requests. |

## Sub-file index

| Trigger | File |
|---|---|
| `dotnet ef migrations` (add/update/script/bundle), design-time factory, `migrationBuilder.InsertData`, model-snapshot conflicts | [migrations-seeding.md](migrations-seeding.md) |
| Value converters, `ValueComparer<T>`, JSON columns, owned types, TPH/TPT/TPC, shadow properties | [modeling.md](modeling.md) |
| `AsSplitQuery`, `EF.CompileAsyncQuery`, `IAsyncEnumerable`, `AsNoTracking`, `ExecuteUpdateAsync`, `ExecuteDeleteAsync`, projection, `TagWith`, `IgnoreAutoIncludes` | [queries.md](queries.md) |
| `SaveChangesInterceptor` (audit, soft-delete, domain events), `EnableRetryOnFailure`, deadlocks, TLS handshake | [interceptors-resiliency.md](interceptors-resiliency.md) |
| Symptom → cause → fix table | [troubleshooting.md](troubleshooting.md) |

## Quick decision matrix

| Scenario | Choice |
|---|---|
| Stateless Web API / gRPC consumer | `AddDbContextPool<T>` |
| Blazor Server, batch worker | `AddDbContextFactory<T>` |
| Context injects `ICurrentUser` or other scoped | `AddDbContext<T>` |
| 0–1 collection in `Include` chain | Single query (default) |
| 2+ collections in `Include` chain | `AsSplitQuery()` (mandatory) |
| Read-only LINQ to DTO | `Select(...)` projection + `AsNoTracking()` |
| Bulk update / delete without graph | `ExecuteUpdateAsync` / `ExecuteDeleteAsync` |
| Soft-delete entity | Interceptor + `HasQueryFilter` |
| Cross-cutting timestamps + user | Audit interceptor with `TimeProvider` |
| Polymorphic, mostly-shared columns | TPH (default) |
| Polymorphic, queries only over concrete subtype | TPC |

## Cross-references

- `dotnet-aspire` — AppHost-side resource wiring (`AddSqlServer`, `AddDatabase`, `WaitFor`, `RunAsEmulator`).
- `dotnet-aspire` § test-seeding — test-time data setup against a running fixture.
- `dotnet-conventions` — technology-named adapter project placement and hexagonal layering.
- `dotnet-hexagonal-architecture` § core-and-infrastructure § Mappers — hand-written `IXxxMapper` services for DTO/entity projection (cross-link from `queries.md`).

## Upstream references

- https://learn.microsoft.com/en-us/ef/core/ — canonical EF Core docs.
- https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/whatsnew — EF Core 10 release notes.
- https://learn.microsoft.com/en-us/ef/core/cli/dotnet — `dotnet ef` CLI reference.
- https://learn.microsoft.com/en-us/ef/core/modeling/value-comparers — value converters and comparers.
- https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/interceptors — interceptors.
- https://learn.microsoft.com/en-us/ef/core/miscellaneous/connection-resiliency — connection resiliency.
- https://learn.microsoft.com/en-us/ef/core/modeling/relationships/owned-entities — owned entities and JSON columns.
