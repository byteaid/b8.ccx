---
name: aspnet-core-data-driven
description: ASP.NET Core 10 + EF Core 10 data-driven web apps (UI/consumption layer over EF). Covers scaffolding (`dotnet aspnet-codegenerator`, `dotnet ef dbcontext scaffold`), Razor Pages + MVC CRUD, overposting mitigation (`TryUpdateModelAsync` allow-list / `[Bind]` / view-model + `PropertyValues.SetValues`), `IFormFile` uploads, sort/filter/page (`PaginatedList<T>`), DbContext lifetime (`AddDbContext`/`AddDbContextPool`/`AddDbContextFactory`), optimistic concurrency, Dapper companion for hot reads, Aspire `AddSqlServerDbContext` consumer, design-time factory, Blazor data-binding (`EditForm`, `@bind-Value`).
when_to_use: |
  - Trigger keywords: Razor Pages CRUD, MVC controller scaffold, dotnet aspnet-codegenerator, dotnet ef dbcontext scaffold, AddDbContext, AddDbContextPool, AddDbContextFactory, IDbContextFactory, IDesignTimeDbContextFactory, TryUpdateModelAsync, [Bind], IFormFile, PaginatedList, AsNoTracking, DbUpdateConcurrencyException, [Timestamp], rowversion, AddSqlServerDbContext, AddNpgsqlDbContext, EditForm, Dapper companion.
  - Task shapes: scaffold Razor Pages or MVC CRUD over an entity; scaffold a `DbContext` from a database; pick the right DbContext lifetime; write a CRUD page that resists overposting; add sort + filter + pagination; handle `IFormFile` uploads bound to EF; build an optimistic-concurrency edit page; wire a consumer through Aspire; add a Dapper read-model sharing a transaction with EF; consume EF from Blazor without leaking `DbContext`.
allowed-tools: Bash, Edit, Glob, Grep, Monitor, NotebookEdit, PowerShell, Read, Write
user-invocable: false
paths: ["**/*.cs", "**/*.cshtml", "**/*.razor", "**/Program.cs", "**/appsettings*.json"]
---

# ASP.NET Core Data-Driven Web Apps — Reference

Reference for the UI/consumption layer over EF Core 10 on ASP.NET Core 10. Pin the rules; load the matching sub-file for depth. For the `DbContext` itself (model config, migrations, query semantics, EF Core 10 features) load `dotnet-ef-core`.

## Mental model

- **The page/controller talks to `DbContext`. The `DbContext` talks to the database.** This skill owns the page-and-controller side; `dotnet-ef-core` owns the DbContext.
- **DbContext is not thread-safe.** One DbContext per request in Razor Pages / MVC (scoped). One DbContext per render (or per logical operation) in Blazor — via `IDbContextFactory<T>` because circuits live for the whole connection and a scoped DbContext would leak.
- **Each web request gets a NEW DbContext** -> entities returned to the previous request are detached. Edit handlers must fetch the row, mutate, save.
- **Async only when SQL fires.** `Where`/`OrderBy`/`Select`/`Include` are not async — they only mutate `IQueryable<T>`. Only `ToListAsync`/`First*Async`/`Single*Async`/`FindAsync`/`Count/AnyAsync`/`SaveChangesAsync`/`Execute*Async`/`AsAsyncEnumerable()` actually issue SQL.
- **Overposting is a real attack vector.** Forms can post fields the model never shows. Mitigate via allow-list (`TryUpdateModelAsync` / `[Bind]`) or — preferred — bind a view-model that contains only UI fields and copy with `PropertyValues.SetValues`.
- **Optimistic concurrency** is a UI flow as much as a DB flow. The hidden `ConcurrencyToken` field round-trips with the form; on `DbUpdateConcurrencyException` re-render the edit with current DB values.

## Non-negotiable rules

1. **`AsNoTracking()` for reads that won't be saved back.** ~30% faster, lower memory.
2. **Don't bind domain entities to forms when it matters.** Use a view-model / DTO, OR an explicit allow-list (`TryUpdateModelAsync(... s => s.X, s => s.Y)` / `[Bind("X,Y")]`).
3. **Razor Pages auto-applies `[ValidateAntiForgeryToken]` on non-GET handlers.** MVC controllers must add it explicitly on POSTs (or `[AutoValidateAntiforgeryToken]` filter).
4. **Pick one DbContext lifetime per app shape — don't mix.**
   - Razor Pages / MVC: `AddDbContext<T>` (scoped, default).
   - High throughput after profiling: `AddDbContextPool<T>` (constructor must take only `DbContextOptions<T>`; no captured scoped state).
   - Blazor Server / parallel work / background services: `AddDbContextFactory<T>` (or `AddPooledDbContextFactory<T>` at scale).
5. **Don't `await` two EF operations in parallel on the same `DbContext`.** Not thread-safe.
6. **Don't call `Database.Migrate()` from `Program.cs` in scaled-out farms.** Multiple instances racing. Use idempotent SQL scripts or migration bundles.
7. **Cap pagination size and expose `PageSize` via configuration** so ops can throttle. Don't accept unbounded `pageSize` from the URL.
8. **Form posts that include filter/sort state use GET** (so the URL is bookmarkable). Reset `pageIndex` to 1 whenever the search string changes.
9. **`MultipartBodyLengthLimit` (and `RequestSizeLimitAttribute`) MUST be set** before accepting `IFormFile`. Stream large files to disk/blob via `MultipartReader` instead of full-buffering through `IFormFile`.
10. **Composite/single fetch picker:** `FindAsync(key)` for PK lookup with no `Include` (hits change tracker first); `FirstOrDefaultAsync(predicate)` for the generic case; `SingleOrDefaultAsync(predicate)` only when uniqueness is asserted (throws on >1).
11. **Refresh the antiforgery token after authentication** — it's bound to user identity.
12. **Compose `IQueryable` then materialize once** — avoid multiple `ToListAsync` round-trips.
13. **Pin EF Core provider version + `Microsoft.Data.SqlClient`** to a known-good combo. .NET 10 is LTS; pin to LTS-only stack.

## Sub-index

| Topic | File | Load when |
|---|---|---|
| NuGet packages, `appsettings.json`, `AddDbContext`/`Pool`/`Factory`, `EnsureCreated` vs migrations, Razor Pages + MVC scaffold commands, `dotnet ef dbcontext scaffold` | [setup-and-lifetime.md](setup-and-lifetime.md) | Setting up a new web tier; choosing the DbContext lifetime; bootstrapping seed/migrations; running scaffolders. |
| Razor Pages CRUD (Read / Create / Edit / Delete) and MVC controller CRUD; overposting mitigation (`TryUpdateModelAsync` allow-list / `[Bind]` / view-model + `PropertyValues.SetValues`) | [crud-and-overposting.md](crud-and-overposting.md) | Writing or reviewing CRUD pages/controllers; designing form binding to resist overposting. |
| `IFormFile` uploads bound to EF; sort + filter + `PaginatedList<T>`; optimistic concurrency UI flow; Dapper companion sharing the EF connection / transaction | [uploads-pagination-concurrency.md](uploads-pagination-concurrency.md) | Adding uploads, paginated lists, an edit page that handles `DbUpdateConcurrencyException`, or a Dapper read-model. |
| Aspire consumer wiring (`AddSqlServerDbContext`, `AddSqlServerClient`, `AddNpgsqlDbContext`); Aspire migrations strategies; design-time factory; Blazor render-mode-aware `IDbContextFactory<T>`; `EditForm` over EF entity | [aspire-and-blazor.md](aspire-and-blazor.md) | Consuming a DB through an Aspire AppHost or wiring data binding from a Blazor component. |

## Quick decision matrix

| Question | Answer |
|---|---|
| Web app, page-based, single DbContext per request | `AddDbContext<T>` |
| Web app, controller-based | `AddDbContext<T>` |
| Web app, profiling shows DI cost dominates | `AddDbContextPool<T>` (constructor restrictions apply) |
| Blazor Server | `AddDbContextFactory<T>` (or pooled at scale) |
| Background service / parallel work | `AddDbContextFactory<T>` |
| Read-only reads | `AsNoTracking()` |
| PK lookup, no `Include` | `FindAsync(key)` |
| Generic single fetch | `FirstOrDefaultAsync(predicate)` |
| Asserted unique fetch | `SingleOrDefaultAsync(predicate)` |
| Filter resists overposting | View-model OR `TryUpdateModelAsync` allow-list OR `[Bind]` |
| Multi-instance Aspire | Migration worker + design-time factory; don't `Database.Migrate()` at startup |
| Hot-path read query, custom shape | Dapper sharing the EF connection / transaction |
| Need to know what changed when concurrency throws | `entry.GetDatabaseValues().ToObject()` |

## Cross-references

- Public docs (Data overview): https://learn.microsoft.com/en-us/aspnet/core/data/?view=aspnetcore-10.0
- Public docs (Razor Pages + EF tutorials): https://learn.microsoft.com/en-us/aspnet/core/data/ef-rp/intro?view=aspnetcore-10.0
- Public docs (CRUD): https://learn.microsoft.com/en-us/aspnet/core/data/ef-rp/crud?view=aspnetcore-10.0
- Public docs (Sort/Filter/Page): https://learn.microsoft.com/en-us/aspnet/core/data/ef-rp/sort-filter-page?view=aspnetcore-10.0
- Public docs (Migrations): https://learn.microsoft.com/en-us/aspnet/core/data/ef-rp/migrations?view=aspnetcore-10.0
- Public docs (Concurrency): https://learn.microsoft.com/en-us/aspnet/core/data/ef-rp/concurrency?view=aspnetcore-10.0
- Public docs (EF in MVC): https://learn.microsoft.com/en-us/aspnet/core/data/ef-mvc/intro?view=aspnetcore-10.0
- Public docs (Aspire SQL Server integration): https://learn.microsoft.com/en-us/dotnet/aspire/database/sql-server-integration?tabs=dotnet-cli
- Public docs (EF Core CLI): https://learn.microsoft.com/en-us/ef/core/miscellaneous/cli/dotnet
- Public docs (What's New EF Core 10): https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/whatsnew
- Related skill: `dotnet-ef-core` — `DbContext` config, model conventions, query semantics, EF Core 10 features (`LeftJoin`, vector, JSON, complex types, `ExecuteUpdateAsync`).
- Related skill: `aspnet-core-blazor` — render modes, lifecycle, `OwningComponentBase`, `@bind*` semantics.
- Related skill: `aspnet-core-security` — antiforgery deep-dive, BFF, `MapIdentityApi` for SPA backends.
- Related skill: `aspnet-core-mvc` / `aspnet-core-razor-pages` — non-data MVC / Razor Pages topics (filters, conventions, partial pages).
- Related skill: `dotnet-aspire` — AppHost wiring, `AddSqlServer`, `AddDatabase`, `WithReference`, `WaitFor`.
- Related skill: `dotnet-conventions` § project-layout/data-access-projects — where the `DbContext` lives in a hexagonal layout.
- Related skill: `dotnet-asynchronous-programming` — `async`/`await` and `IAsyncEnumerable<T>` semantics EF relies on.
