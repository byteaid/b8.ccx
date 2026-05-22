# Query patterns — split, compiled, streaming, bulk, projection

> Prerequisite: [skill.md](skill.md). Default posture: `AsNoTracking()` for reads, `Select(...)` to a DTO when not all columns are needed, async all the way.

## Split queries (mandatory with 2+ collection Includes)

Single-query Includes with multiple collections produce a Cartesian product (`COUNT(parent) * COUNT(items) * COUNT(payments)` rows over the wire). Split queries issue one SQL statement per collection and stitch results client-side.

```csharp
var orders = await db.Orders
    .Include(o => o.Items)
    .Include(o => o.Payments)   // 2nd collection → Cartesian without split
    .AsSplitQuery()
    .ToListAsync(ct);
```

Global setting (preferred when the codebase is consistently split-friendly):

```csharp
opts.UseSqlServer(cs, sql => sql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));
```

When global split is on, use `.AsSingleQuery()` for the rare case where a Cartesian is actually cheaper (single-row parent with tiny child collections).

**Team rule:** without split, Cartesian explosion is invisible in dev with seeded data and pathological in prod with real volumes. Treat 2+ collection `Include` without `AsSplitQuery()` as a code-review block.

## Projection (avoid over-fetching)

Project to a DTO when the API surface only needs a subset of columns. Cuts wire bytes, removes the need for tracking, and lets the SQL planner pick a covering index.

```csharp
var summaries = await db.Invoices
    .AsNoTracking()
    .Where(x => x.CustomerId == customerId)
    .Select(x => new InvoiceSummaryDto(x.Id, x.Number, x.Total, x.Status))
    .ToListAsync(ct);
```

For complex DTOs with nested shapes, hand-write the `Select` expression — or extract the mapping to a hand-written `IXxxMapper` service. See `dotnet-hexagonal-architecture` § core-and-infrastructure § Mappers for the canonical first-party pattern. Source-generated and convention-based mappers (Mapperly, AutoMapper, Mapster) are banned — see `dotnet-conventions` § forbidden-patterns § no-automapper-no-mediatr.

## `IgnoreAutoIncludes` for surgical projections

When the model has `Navigation().AutoInclude()` configured (auto-loaded reference data) and a specific query does not need it:

```csharp
var rows = await db.Invoices
    .IgnoreAutoIncludes()
    .Where(x => x.Id == id)
    .Select(x => new { x.Id, x.Total })
    .ToListAsync(ct);
```

Without it, `AutoInclude` joins fire even though the projected DTO doesn't reference them.

## Compiled queries (hot path)

`EF.CompileAsyncQuery` caches the LINQ-to-SQL translation in a delegate. Saves the per-call expression-tree compilation. Worth it for queries on the request-per-second hot path.

```csharp
private static readonly Func<BillingDb, Guid, CancellationToken, Task<Invoice?>> GetInvoiceById =
    EF.CompileAsyncQuery((BillingDb db, Guid id, CancellationToken ct) =>
        db.Invoices.AsNoTracking().FirstOrDefault(x => x.Id == id));

public Task<Invoice?> FindAsync(Guid id, CancellationToken ct) => GetInvoiceById(db, id, ct);
```

Limitations: parameters must be value types or string; complex predicates with optional filters do not compile. Profile first — the win is real but small (5–15% per call) and the code is less flexible.

## Streaming with `IAsyncEnumerable<T>`

Stream large result sets without materializing the full list. The EF query stays open for the duration of the iteration, so don't hold it across long-running work or other queries on the same context.

```csharp
await foreach (var invoice in db.Invoices.AsNoTracking().AsAsyncEnumerable().WithCancellation(ct))
{
    // process row-by-row; SqlClient pages internally
}
```

For service-method shapes:

```csharp
public IAsyncEnumerable<Invoice> GetByCustomerAsync(Guid customerId, CancellationToken ct) =>
    db.Invoices
      .AsNoTracking()
      .Where(x => x.CustomerId == customerId)
      .AsAsyncEnumerable();
```

The consumer iterates with `await foreach` and can short-circuit; rows are pulled lazily.

## Tracking modes

| Mode | When |
|---|---|
| `AsNoTracking()` | Default for reads. No identity map, no change detection. |
| `AsNoTrackingWithIdentityResolution()` | Read query that returns the same row multiple times in a graph (e.g. self-referential `ParentId`). Single instance per key, still no tracking. |
| Tracking (default, no opt-in needed) | Read-modify-save in the same scope. |

Calling `db.ChangeTracker.Clear()` between unrelated reads on the same context releases tracked entries.

## `ExecuteUpdateAsync` / `ExecuteDeleteAsync` (bulk, no tracking)

Set-based update/delete that translates directly to a single SQL statement. **Skip the change tracker, skip `SaveChanges` interceptors.**

```csharp
await db.Invoices
    .Where(x => x.Status == InvoiceStatus.Draft && x.CreatedAt < cutoff)
    .ExecuteDeleteAsync(ct);

await db.Invoices
    .Where(x => x.Status == InvoiceStatus.Pending)
    .ExecuteUpdateAsync(s => s
        .SetProperty(x => x.Status, InvoiceStatus.Cancelled)
        .SetProperty(x => x.UpdatedAt, DateTimeOffset.UtcNow), ct);
```

**Heads up — interceptor bypass:** if `AuditInterceptor` writes `UpdatedAt`/`UpdatedBy`, `ExecuteUpdateAsync` will not run it. Set those columns explicitly inside the `SetProperty` chain. Same applies to `SoftDeleteInterceptor`: prefer `ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted, true))` over `ExecuteDeleteAsync` for soft-deletable entities.

`ExecuteUpdateAsync` ignores any in-memory mutations on tracked entities — it issues SQL based on the predicate, not on the change tracker. Mixing tracked changes with `ExecuteUpdateAsync` in the same scope is a bug.

## `TagWith` for query identification

Annotate a query with a tag that surfaces in SQL as a leading comment. Makes log/profiler diagnosis trivial.

```csharp
var rows = await db.Invoices
    .TagWith("InvoiceQueryService.GetByCustomerWithItems")
    .AsNoTracking()
    .Where(x => x.CustomerId == customerId)
    .Include(x => x.Items)
    .ToListAsync(ct);
```

Resulting SQL starts with `-- InvoiceQueryService.GetByCustomerWithItems`. Use `TagWithCallSite()` (EF 7+) to capture the file/line automatically.

## Avoid client evaluation

Operations EF cannot translate run client-side and pull the entire query result into memory. EF 3.0+ throws by default; some shapes still slip through (e.g. user-defined methods inside `Where`).

| Symptom | Fix |
|---|---|
| `The LINQ expression '...' could not be translated` | Use `EF.Functions.Like(...)`, `EF.Functions.Collate(...)`, or refactor the predicate to translatable members. |
| Predicate uses a custom method | Inline the logic, or pre-compute the value on the entity. |
| Need a calculation EF can't translate | Project the raw columns first, then `AsEnumerable()` and compute client-side — only when the dataset is bounded. |

## Quick rules

- `AsNoTracking()` by default on reads.
- `AsSplitQuery()` mandatory with 2+ collection `Include`s.
- Project to DTO when the API surface doesn't need the full graph.
- `ExecuteUpdateAsync` / `ExecuteDeleteAsync` for set-based mutations — accept the interceptor bypass and set audit columns inline.
- `IAsyncEnumerable<T>` for streams; never materialize a million-row table into a `List<T>`.
- Compiled queries only on profiled hot paths.
- `TagWith` every non-trivial query that hits prod.
- Never `.Result` / `.Wait()`.
