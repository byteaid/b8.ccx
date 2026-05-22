# Troubleshooting — symptom, cause, fix

> Prerequisite: [skill.md](skill.md). Cross-references point to the chapter that holds the canonical fix.

## Migrations and design-time

| Symptom | Probable cause | Fix |
|---|---|---|
| `Unable to create a DbContext of type 'X'` on `migrations add` | Missing `IDesignTimeDbContextFactory<T>` in the `[Company].[Product].{TechName}` adapter project, or the factory is `internal` | [migrations-seeding § Design-time factory](migrations-seeding.md). Class must be `public`, parameterless. |
| `Migration 'X' applies but does nothing` (rows not inserted) | Migration only modified the snapshot; the body was wiped on a merge conflict | Open the generated `Up` method — if it's empty, the merge ate it. Regenerate: `migrations remove`, then `migrations add` again. |
| Snapshot conflict on merge (both branches changed `*ContextModelSnapshot.cs`) | Two `migrations add` operations on parallel branches | [migrations-seeding § Model-snapshot conflicts](migrations-seeding.md). Resolve by removing one migration, rebasing, regenerating. |
| `dotnet ef migrations has-pending-model-changes` reports drift in CI | Snapshot out of sync with `OnModelCreating` | Run `migrations add Reconcile`; review the diff before merging. |
| Migration applies in dev, fails in prod with `cannot alter column ...` | Existing data is incompatible with the new column constraint | Two-step migration: add nullable column → backfill data → migrate to NOT NULL. |
| `Database.MigrateAsync()` racy across replicas in prod | `MigrateAsync` on app boot in a replicated deployment | Run migrations as a separate Aspire `AddExecutable` resource with `WaitForCompletion(migrator)` blocking the API. See `dotnet-aspire`. |

## Tracking and change detection

| Symptom | Probable cause | Fix |
|---|---|---|
| `The instance of entity type 'Y' cannot be tracked because another instance with the same key value is already being tracked` | Two read paths in the same scope materialized the same row, or stale tracked entries from a prior call | `AsNoTracking()` on the read query, or `db.ChangeTracker.Clear()` between calls. For read graphs with self-references, `AsNoTrackingWithIdentityResolution()`. |
| `SaveChangesAsync` returns 0 with no error after a clear mutation | Property uses a value converter without a `ValueComparer<T>`; change tracker compares by reference and misses the mutation | [modeling § Value converters](modeling.md). Set `Metadata.SetValueComparer(comparer)` on the property. |
| `ExecuteUpdateAsync` ignores in-memory tracked changes | Bulk SQL bypasses the change tracker by design | Either `SaveChangesAsync` first then `ExecuteUpdateAsync` for the rest, or move all mutations to `SetProperty`. Don't mix. |
| Audit columns not populated on bulk update | `SaveChangesInterceptor` doesn't run for `ExecuteUpdateAsync` | Set `UpdatedAt` / `UpdatedBy` explicitly in the `SetProperty` chain. See [queries](queries.md). |

## Query performance

| Symptom | Probable cause | Fix |
|---|---|---|
| Slow query with `Include(.Children).Include(.Things)`; row count = `parents × children × things` | Cartesian explosion from 2+ collection Includes in a single query | `.AsSplitQuery()` per query, or `UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)` globally. See [queries](queries.md). |
| N+1 in logs (`SELECT * FROM child WHERE parent_id = @p0` repeated) | Lazy-loading proxies enabled, or missing `Include` | Project to DTO, or add `Include`. Disable lazy loading globally. |
| `The LINQ expression '...' could not be translated` | Predicate uses a CLR method EF can't translate | `EF.Functions.Like(...)`, `EF.Functions.Collate(...)`, refactor to translatable members, or `AsEnumerable()` for client evaluation when the dataset is bounded. |
| OOM on large query | `ToListAsync` on a million-row table | `IAsyncEnumerable<T>` streaming. See [queries](queries.md). |
| Connection pool exhausted (`Timeout expired ... pool was full`) | Forgotten `DbContext` not disposed, or pool too small | `using` / `await using`. Audit `IDbContextFactory<T>` consumers. Raise `MaxPoolSize` only after a leak is ruled out. |

## Modeling

| Symptom | Probable cause | Fix |
|---|---|---|
| `Circular reference detected` serializing TPH discriminator | Newtonsoft/STJ serializing the navigation back-reference | DTO projection. Don't serialize entities directly. |
| JSON column round-trip mismatch (saved value differs from in-memory) | Owned-type property has a default that's set in the constructor but not the JSON deserializer path | Use `init`-only properties on the owned type, or move defaults into the EF mapping. |
| `Foreign key constraint failed` on insert | Referenced principal not yet inserted; FK is shadow on the dependent | Insert the principal first, or attach it as `EntityState.Unchanged` if it already exists. |
| Owned type `OwnsOne` columns not appearing in migration | Configuration applied via `IEntityTypeConfiguration<T>` not registered | Confirm `modelBuilder.ApplyConfigurationsFromAssembly(...)` is called. |

## Resiliency and transactions

| Symptom | Probable cause | Fix |
|---|---|---|
| `The configured execution strategy 'SqlServerRetryingExecutionStrategy' does not support user-initiated transactions` | `EnableRetryOnFailure` + direct `BeginTransactionAsync` | Wrap with `db.Database.CreateExecutionStrategy().ExecuteAsync(...)`. See [interceptors-resiliency](interceptors-resiliency.md). |
| Deadlock retry loop never terminates | Two paths consistently take locks in opposite order | Order writes consistently across paths; keep transactions short; consider `READ COMMITTED SNAPSHOT` at the database level. |
| Custom RAISERROR not retried | Custom error number not in the transient list | Pass it via `errorNumbersToAdd` of `EnableRetryOnFailure`. |

## Containers and TLS

| Symptom | Probable cause | Fix |
|---|---|---|
| `The certificate chain was issued by an authority that is not trusted` connecting to containerized SQL Server | Self-signed cert from `mcr.microsoft.com/mssql/server` image | `SqlConnectionStringBuilder { TrustServerCertificate = true }` on the consumer in `Program.cs`. See [interceptors-resiliency § TLS](interceptors-resiliency.md). |
| Same TLS error in prod against a managed SQL instance | `TrustServerCertificate = true` was hard-coded everywhere | Branch on `IsDevelopment()` or use a config flag; managed SQL has a real cert and should reject untrusted CAs. |
| Connection succeeds locally but fails in CI | CI's DNS / firewall blocks the SQL container port | Check Aspire dashboard for the actual mapped port; CI must use the dashboard-reported CS. |

## Tooling

| Symptom | Probable cause | Fix |
|---|---|---|
| `dotnet ef` not found | Tool not installed | `dotnet tool install --global dotnet-ef`. |
| `dotnet ef --version` reports old version | Global tool stale | `dotnet tool update --global dotnet-ef`. |
| Migration commands hang | Design-time factory tries to connect to a real DB that's down | Make the factory CS purely synthetic — EF only needs it to compile the model, not to connect. See [migrations-seeding § Design-time factory](migrations-seeding.md). |
| `--startup-project` complaints about ambiguous DbContext | Multiple `DbContext` types in the assembly | Pass `--context BillingDb` explicitly. |
