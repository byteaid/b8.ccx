# Interceptors, resiliency, TLS

> Prerequisite: [skill.md](skill.md). All cross-cutting `SaveChanges` behavior (audit, soft-delete, domain events) goes through `SaveChangesInterceptor`. Never override `SaveChanges()` on the `DbContext` — overrides do not compose.

## Audit interceptor (Created/Updated)

```csharp
public sealed class AuditInterceptor(TimeProvider timeProvider, ICurrentUser currentUser) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        var ctx = eventData.Context;
        if (ctx is null) return base.SavingChangesAsync(eventData, result, ct);

        var now = timeProvider.GetUtcNow();
        var userId = currentUser.Id;

        foreach (var entry in ctx.ChangeTracker.Entries<IAuditable>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.CreatedBy = userId;
                    entry.Entity.UpdatedAt = now;
                    entry.Entity.UpdatedBy = userId;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    entry.Entity.UpdatedBy = userId;
                    entry.Property(x => x.CreatedAt).IsModified = false;
                    entry.Property(x => x.CreatedBy).IsModified = false;
                    break;
            }
        }
        return base.SavingChangesAsync(eventData, result, ct);
    }
}
```

Rules:

- **`TimeProvider` not `DateTime.UtcNow`.** Wall-clock reads are untestable; `TimeProvider` lets tests inject a controlled clock.
- **`Guid.CreateVersion7()` for new IDs** when the entity needs a generated key in domain code (.NET 9+). UUIDv7 is sortable and index-friendly.
- Reset `CreatedAt`/`CreatedBy` `IsModified` to false on `EntityState.Modified` so callers cannot accidentally overwrite them.

Registration:

```csharp
builder.Services.AddScoped<AuditInterceptor>();
builder.Services.AddDbContextPool<BillingDb>((sp, opts) =>
{
    opts.UseSqlServer(csBuilder.ConnectionString);
    opts.AddInterceptors(sp.GetRequiredService<AuditInterceptor>());
}, poolSize: 1024);
```

The `(sp, opts) =>` overload is required for pooled contexts — it gives the interceptor a per-scope service provider while keeping the context pooled.

## Soft-delete interceptor + global query filter

Two pieces working together: an interceptor that converts `EntityState.Deleted` into a flag update, and a query filter that hides soft-deleted rows from every `SELECT`.

```csharp
public sealed class SoftDeleteInterceptor(TimeProvider timeProvider) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        if (eventData.Context is null) return base.SavingChangesAsync(eventData, result, ct);

        var now = timeProvider.GetUtcNow();
        foreach (var entry in eventData.Context.ChangeTracker.Entries<ISoftDeletable>())
        {
            if (entry.State == EntityState.Deleted)
            {
                entry.State = EntityState.Modified;
                entry.Entity.IsDeleted = true;
                entry.Entity.DeletedAt = now;
            }
        }
        return base.SavingChangesAsync(eventData, result, ct);
    }
}
```

```csharp
// OnModelCreating — apply per ISoftDeletable entity
modelBuilder.Entity<Invoice>().HasQueryFilter(x => !x.IsDeleted);
```

To bypass the filter for an admin/audit query: `.IgnoreQueryFilters()`.

**Heads up:** `ExecuteDeleteAsync` skips the interceptor — see [queries](queries.md). Use `ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted, true))` to soft-delete in bulk.

## Domain-event dispatcher

Aggregate roots collect domain events; the interceptor drains and dispatches them after the DB commit succeeds. Dispatch is async via a channel/queue — **not** synchronous via in-memory MediatR — so a slow handler does not block `SaveChanges`.

```csharp
public sealed class DomainEventInterceptor(IDomainEventDispatcher dispatcher) : SaveChangesInterceptor
{
    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken ct = default)
    {
        var ctx = eventData.Context;
        if (ctx is null) return await base.SavedChangesAsync(eventData, result, ct);

        var entities = ctx.ChangeTracker.Entries<IHasDomainEvents>()
            .Select(e => e.Entity)
            .Where(e => e.DomainEvents.Count > 0)
            .ToArray();

        foreach (var entity in entities)
        {
            var events = entity.DomainEvents.ToArray();
            entity.ClearDomainEvents();
            foreach (var ev in events)
                await dispatcher.DispatchAsync(ev, ct);
        }
        return await base.SavedChangesAsync(eventData, result, ct);
    }
}
```

Pick the interceptor method by intent:

| Method | When | Use case |
|---|---|---|
| `SavingChangesAsync` | **Before** the DB commit | Audit fields, soft-delete, validation, transactional outbox writes (same TX as the data). |
| `SavedChangesAsync` | **After** the DB commit succeeds | Publishing domain events to external systems, cache invalidation, metrics. |
| `SaveChangesFailedAsync` | After commit failure | Telemetry, distributed-tracing failure span, dead-letter logging. |

Outbox pattern: write the outbox row in `SavingChangesAsync` so it commits with the data; a separate dispatcher reads the outbox and publishes externally. Putting the publish in `SavedChangesAsync` risks losing events if the host crashes between commit and publish.

## Connection resiliency — `EnableRetryOnFailure`

```csharp
opts.UseSqlServer(cs, sql =>
    sql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null));
```

EF retries on the SQL Server transient-error list (deadlocks SqlException 1205, transient connection failures, etc.). The retry policy is per-execution: a single LINQ `ToListAsync`, a single `SaveChangesAsync`, a single `ExecuteUpdateAsync`.

### User-initiated transactions + retry

`EnableRetryOnFailure` and `db.Database.BeginTransactionAsync(...)` are mutually incompatible by default — EF throws `The configured execution strategy 'SqlServerRetryingExecutionStrategy' does not support user-initiated transactions`. Wrap the transaction in an execution strategy:

```csharp
var strategy = db.Database.CreateExecutionStrategy();
await strategy.ExecuteAsync(async () =>
{
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    await db.Invoices.AddAsync(invoice, ct);
    await db.SaveChangesAsync(ct);
    await db.Payments.AddAsync(payment, ct);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
});
```

The strategy retries the **whole delegate** on transient failure — design it to be idempotent.

### Custom transient errors

If a stored procedure raises a custom error number that the retry policy should treat as transient:

```csharp
sql.EnableRetryOnFailure(maxRetryCount: 5,
    maxRetryDelay: TimeSpan.FromSeconds(10),
    errorNumbersToAdd: new[] { 50001, 50002 });
```

### Deadlocks (SqlException 1205)

Already in the default transient list. With retry on, deadlocks recover automatically up to `maxRetryCount`. To reduce deadlocks, order writes consistently across code paths and keep transactions short.

## TLS — containerized SQL Server (consumer-side fix)

**Symptom:** `A connection was successfully established with the server, but then an error occurred during the login process. (provider: SSL Provider, error: 0 - The certificate chain was issued by an authority that is not trusted.)`

**Cause:** the official SQL Server image (`mcr.microsoft.com/mssql/server:2022-latest`) signs its server certificate with a self-signed CA. Microsoft.Data.SqlClient since 4.x requires a trusted certificate by default (`Encrypt=true`, `TrustServerCertificate=false`).

**Fix:** on the **consumer**, set `TrustServerCertificate=true` via `SqlConnectionStringBuilder` in `Program.cs` (see [skill.md § DbContext registration](skill.md)). Three things this fix is **not**:

- Not done on the AppHost. Aspire emits the raw CS without `TrustServerCertificate`. The flag is the consumer's concern. AppHost-side rationale (which container image, why the cert is self-signed) lives in `dotnet-aspire`.
- Not put in `appsettings.json`. The CS comes from Aspire at runtime; nothing in static config sees it.
- Not unconditional. In production with a managed SQL instance and a real cert, `TrustServerCertificate` should be `false`. Branch on environment if needed:

```csharp
var csBuilder = new SqlConnectionStringBuilder(rawCs)
{
    TrustServerCertificate = builder.Environment.IsDevelopment(),
    Encrypt = true,
};
```

## Interceptors do not modify SQL

`DbCommandInterceptor` can rewrite SQL but the team rule is to **not** use it for that. Concerns it might be tempted to handle, with the correct alternative:

| Need | Wrong tool | Right tool |
|---|---|---|
| Computed column | SQL rewrite | Migration with `computedColumnSql` |
| Concurrency token | SQL rewrite | `IsRowVersion()` |
| Multi-tenant filtering | SQL rewrite | Global query filter (`HasQueryFilter`) |
| Audit columns | SQL rewrite | `SaveChangesInterceptor` |
| Soft delete | SQL rewrite | `SaveChangesInterceptor` + query filter |
