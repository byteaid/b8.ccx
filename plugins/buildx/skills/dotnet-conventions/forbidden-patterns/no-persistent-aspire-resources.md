# Forbidden — Persistent Aspire resources

## What it looks like

```csharp
// AppHost/Program.cs
var sql = builder.AddSqlServer("sql")
    .WithDataVolume()                                          // banned
    .WithLifetime(ContainerLifetime.Persistent);                // banned

var pg = builder.AddPostgres("pg")
    .WithDataBindMount("/var/data/pg")                          // banned
    .WithInitBindMount("./init-scripts");                       // obsolete API too

var mongo = builder.AddMongoDB("mongo")
    .WithBindMount("/data/db", isReadOnly: false);              // data-folder bind mount: banned
```

## Why it's banned

1. **Tests must start from a known state.** Every `aspire run`, every integration-test suite, every CI job starts with a blank container. Persisted data carries cross-suite state into the next run, producing tests that pass on a developer's machine and fail in CI.
2. **Cold-start amortization is a Docker problem, not an Aspire problem.** Image-layer caching keeps `aspire run` fast across reboots without persisting **data**. The container restarts blank in milliseconds; the image is already on disk.
3. **Production persistence is cloud-managed.** Real persistence in production runs on managed services (Azure SQL, Cosmos DB, Postgres flexible server, Redis Cache). The local AppHost should never simulate that — it tests against a stateless mirror.
4. **The team has lived through state-leak incidents.** A `WithDataVolume()` left in for "dev convenience" carried a stale schema across a migration upgrade and produced a 4-hour debug session that ended with `docker volume rm`.

## What to do instead

```csharp
var sql = builder.AddSqlServer("sql")
    .WithLifetime(ContainerLifetime.Session);   // default — ephemeral

var pg = builder.AddPostgres("pg")
    .WithInitFiles("./init-scripts");           // current API; runs SQL once at first boot

var mongo = builder.AddMongoDB("mongo");        // no volume, no bind mount; ephemeral
```

Seeding strategies:

- **Reference data** ships in EF Core migrations or container init scripts (Postgres `WithInitFiles`, MySQL bind-mount to `/docker-entrypoint-initdb.d`). These run on every fresh boot — that's the point.
- **Test data** lives in the test project's `[AssemblyInitialize]` fixture. See `dotnet-aspire` § integration-testing.
- **No `DbInitializer` / `SeedingService`** in the app. See [no-seed-endpoints.md](no-seed-endpoints.md).

## Enforcement

- **On sight, inside the AppHost:** delete the persistence primitives. See [../build-quality/clean-as-you-touch.md](../build-quality/clean-as-you-touch.md).
- **No exception for "dev mode".** `IsRunMode`-guarded persistence is also banned. The rule is absolute.
- **Quick scan:**

  ```bash
  grep -rE "WithDataVolume|WithDataBindMount|ContainerLifetime\.Persistent|WithBindMount.*(\"/data|\"/var/(opt|lib)/(mssql|postgresql|mongodb))" src/
  ```

  must return no matches.

## Obsolete APIs to migrate alongside

- `WithInitBindMount` → `WithInitFiles` (Postgres).
- `appBuilder.Services.AddLifecycleHook<T>()` → `appBuilder.Eventing.Subscribe<TEvent>(...)`.

If you find these while editing the AppHost file, migrate them in the same pass.

## See also

- `dotnet-aspire` — the full Aspire authoring reference.
