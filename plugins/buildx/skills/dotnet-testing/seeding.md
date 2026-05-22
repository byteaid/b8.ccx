# Test Data Seeding

How to load test data into Aspire-orchestrated stateful resources without contaminating production code. Four canonical strategies, picked by resource type and seed complexity. Companion to [mstest-integration.md](mstest-integration.md), which owns the per-class lifecycle; this file owns the seed-data layer.

## Invariants (non-negotiable)

1. **Ephemeral by default, always.** No `WithDataVolume()`, no `WithLifetime(ContainerLifetime.Persistent)`, no `WithBindMount` on data folders. Every `aspire run`, every test class boot, every CI job starts from a blank container. Cold-start cost is amortised by the Docker image-layer cache and by the per-class isolation choice (one host per class is intentional — see [mstest-integration.md](mstest-integration.md)).
2. **No seed logic in application code.** Forbidden in AppHost / Host / API projects:
   - `if (app.Environment.IsEnvironment("Testing")) db.Seed(...)` branches in `Program.cs`.
   - `app.MapPost("/_test/seed", ...)` / `/_test/reset` endpoints guarded by environment.
   - EF Core `UseSeeding` / `UseAsyncSeeding` whose lambdas are meaningless in production.
   - A `SeedService` registered in DI that no production code ever calls.
   - `if (builder.ExecutionContext.IsRunMode) AddFakeXxx()` — that's a stub, route to the stub generator instead.
3. **Seed code lives in the test project**, under `Seeding/`. One file per concern (`SqlSeedData.cs`, `CosmosSeedData.cs`). `internal static class`, `internal static async Task ApplyAsync(...)`. Idempotent.
4. **If production needs seeded data**, that is operational migration data, not test data. Ship via EF Core migrations (`migrationBuilder.InsertData(...)`) or DBA scripts run by the deployment pipeline. Those run identically in prod and in tests.

The AppHost produces the same topology under `aspire run`, `DistributedApplicationTestingBuilder`, and `aspire publish`. The only legitimate fork is `builder.ExecutionContext.IsRunMode` for invisible dev niceties (e.g. opening the dashboard).

## Strategy selection

| Resource / need | Strategy |
|---|---|
| SQL Server, SQL Edge | A — direct client (`SqlConnection` + Dapper / EF Core `DbContext` instantiated in-test) |
| Postgres | B (`WithInitFiles`) for static SQL; A for anything dynamic |
| MySQL / MariaDB | B (`WithBindMount` to `/docker-entrypoint-initdb.d`); A for dynamic |
| MongoDB | A (`MongoClient` from the test) |
| Redis (warming keys, fixtures) | A (`IConnectionMultiplexer` from the test) |
| Azure Cosmos DB | C (emulator + `CosmosClient`) |
| Azure Blob/Queue/Table Storage | C (Azurite + corresponding SDK) |
| Azure Service Bus | C (emulator + `ServiceBusAdministrationClient`) |
| Kafka / RabbitMQ (topic/queue prep) | A (admin client from the test) |
| Cross-resource orchestration; domain-driven seeds | D — eventing subscriber |
| Seed must call the app's real HTTP API (real production endpoint) | D — eventing subscriber |

## Strategy A — Direct client after `StartAsync`

Most controllable. Use whenever the seed is transactional / relational and there is a C# client (`SqlClient`, `Npgsql`, `MongoClient`, `IConnectionMultiplexer`, etc.).

Inside the per-class `[ClassInitialize]`, after `WaitForResourceHealthyAsync`:

```csharp
var cs = await _app.GetConnectionStringAsync("appdb")
         ?? throw new InvalidOperationException("No CS for 'appdb'.");
await SqlSeedData.ApplyAsync(cs, default);
```

```csharp
// test/Acme.Inventory/Acme.Inventory.Test/Seeding/SqlSeedData.cs
using Microsoft.Data.SqlClient;
using Dapper;

namespace Acme.Inventory.Test.Seeding;

internal static class SqlSeedData
{
    public static async Task ApplyAsync(string connectionString, CancellationToken ct)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);

        await conn.ExecuteAsync("""
            IF NOT EXISTS (SELECT 1 FROM Customers WHERE Email = 'admin@test.com')
            BEGIN
                INSERT INTO Customers (Id, Name, Email, AccessLevel)
                VALUES (NEWID(), 'Test User', 'admin@test.com', 'Admin');
            END
            """);
    }
}
```

Rules:

- **Idempotent inserts** (`IF NOT EXISTS`, `ON CONFLICT DO NOTHING`, `UPSERT`) so re-runs under the debugger never fail.
- **Connection string from `_app.GetConnectionStringAsync(name)`** — never hardcode. The name matches `AddDatabase("name")`, not the server name.
- **Schema creation belongs to EF Core migrations or DBA scripts** applied by the real app's startup path or a `WaitForCompletion` migrator resource. The seed only inserts rows.
- **Do not register the `DbContext` in test DI** — instantiate it directly with the Aspire CS. Keep the test out of the app's composition root.
- One file per concern (users, catalogue, permissions). Anything > ~15 lines or > 1 concern → dedicated file.

## Strategy B — Container-native init scripts (Postgres, MySQL)

Postgres and MySQL run every file in `/docker-entrypoint-initdb.d` once on first boot, before accepting connections. When the seed is pure SQL with no runtime logic, this is the cheapest option.

| Engine | API |
|---|---|
| Postgres | `WithInitFiles("TestData/PostgresInit")` — replaces obsolete `WithInitBindMount` (Aspire 13.2+) |
| MySQL | `WithBindMount("TestData/MySqlInit", "/docker-entrypoint-initdb.d", isReadOnly: true)` (no typed `WithInitFiles`) |
| SQL Server | No init folder in the official image. Use Strategy A, or `WithCreationScript(scriptContent)` for a single CREATE. |

The AppHost should not know about test data, so mutate the resource **from the per-class `[ClassInitialize]`** before `BuildAsync`:

```csharp
using Aspire.Hosting.Postgres;

var appHost = await DistributedApplicationTestingBuilder
    .CreateAsync<Projects.Acme_Inventory_AppHost>([]);

var postgres = appHost.Resources
    .OfType<PostgresServerResource>()
    .Single(r => r.Name == "postgres");

var initDir = Path.Combine(AppContext.BaseDirectory, "TestData", "PostgresInit");
appHost.CreateResourceBuilder(postgres).WithInitFiles(initDir);

_app = await appHost.BuildAsync();
await _app.StartAsync();
await _app.ResourceNotifications
    .WaitForResourceHealthyAsync("postgres")
    .WaitAsync(TimeSpan.FromMinutes(3));
```

`.csproj` of the test project must copy the scripts to the output directory:

```xml
<ItemGroup>
  <None Update="TestData\**\*.sql" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

Limits:

- Init scripts run **once, on first container start**, against the default database. Multi-database seeds need `\connect dbname` (Postgres) or `USE dbname;` (MySQL).
- Aspire marks the resource Healthy only after init scripts complete — no race.
- Keep scripts under `TestData/` — never alongside production migrations.

## Strategy C — Emulator + client SDK (Azure)

Azure resources have local emulators wired by the AppHost via `RunAsEmulator()`. Seed via the corresponding SDK after the resource is healthy.

```csharp
await _app.ResourceNotifications
    .WaitForResourceHealthyAsync("cosmos")
    .WaitAsync(TimeSpan.FromMinutes(3));

var cs = await _app.GetConnectionStringAsync("cosmos")
         ?? throw new InvalidOperationException("No CS for 'cosmos'.");
await CosmosSeedData.ApplyAsync(cs, default);
```

```csharp
// test/Acme.Inventory/Acme.Inventory.Test/Seeding/CosmosSeedData.cs
using Microsoft.Azure.Cosmos;

namespace Acme.Inventory.Test.Seeding;

internal static class CosmosSeedData
{
    public static async Task ApplyAsync(string connectionString, CancellationToken ct)
    {
        var options = new CosmosClientOptions
        {
            // Local emulator cert is self-signed.
            HttpClientFactory = () => new HttpClient(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
            }),
            ConnectionMode = ConnectionMode.Gateway,
        };

        using var client = new CosmosClient(connectionString, options);

        var db = await client.CreateDatabaseIfNotExistsAsync("AcmeInventoryDb", cancellationToken: ct);
        var container = await db.Database.CreateContainerIfNotExistsAsync(
            "Profiles", "/profileId", cancellationToken: ct);

        await container.Container.UpsertItemAsync(
            new { id = Guid.CreateVersion7().ToString(), profileId = "p1", isActive = true },
            new PartitionKey("p1"),
            cancellationToken: ct);
    }
}
```

Same shape applies to:

| Resource | Admin / seed client |
|---|---|
| Azure Storage (Azurite) | `BlobServiceClient` / `QueueServiceClient` / `TableServiceClient` |
| Azure Service Bus emulator | `ServiceBusAdministrationClient` to create queues/topics, `ServiceBusSender` to enqueue messages |
| Azure Key Vault emulator | typically no seed — secrets come from `AddParameter` |

**Never call `RunAsEmulator()` from the test.** That belongs to the AppHost. If a resource omits emulator mode, fix the AppHost.

## Strategy D — Eventing subscriber (cross-resource / HTTP-driven)

When the seed must touch several resources in order, or drive the app's real HTTP API to create domain objects, subscribe to an Aspire event from `[ClassInitialize]`. This replaces the obsolete `AddLifecycleHook<T>` API.

```csharp
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Eventing;

var appHost = await DistributedApplicationTestingBuilder
    .CreateAsync<Projects.Acme_Inventory_AppHost>([]);

appHost.Eventing.Subscribe<AfterResourcesCreatedEvent>(async (evt, ct) =>
{
    await DomainSeedData.ApplyAsync(evt.Services, ct);
});

_app = await appHost.BuildAsync();
await _app.StartAsync();
```

Per-resource variant — wait for one specific resource:

```csharp
var db = appHost.Resources.OfType<SqlServerDatabaseResource>().Single(r => r.Name == "appdb");
appHost.Eventing.Subscribe<ResourceReadyEvent>(db, async (evt, ct) =>
{
    var cs = await evt.Services.GetRequiredService<IConfiguration>()
        .GetConnectionStringAsync(db.Name, ct);
    await SqlSeedData.ApplyAsync(cs!, ct);
});
```

When seeding through the app's HTTP API, the endpoint **must be a real production endpoint** exercising real domain logic (`POST /api/v1/customers`). Forbidden: a `/_test/seed` backdoor that only exists in non-prod. If the seed needs something the production API doesn't expose, drop to Strategy A.

**Obsolete — do not introduce.** `appBuilder.Services.AddLifecycleHook<T>()` with `IDistributedApplicationLifecycleHook`. If found in legacy code, flag for migration; do not rip out mid-feature.

## Anti-pattern catalogue

| Anti-pattern | Correct pattern |
|---|---|
| `if (app.Environment.IsEnvironment("Testing")) seeder.Seed()` in `Program.cs` | Remove. Move to Strategy A/D in the test class. |
| `/_test/reset`, `/_test/seed` endpoints guarded by environment | Remove. Seed = test-side; reset = per-test direct SQL or tenant-scoping. |
| `SeedingService` registered in real host DI | Remove. Real reference data → EF migration; fake data → test project. |
| EF Core `UseSeeding(...)` with fake users / fake products | Remove. Real data → migration; tests use Strategy A. |
| `.WithDataVolume()` or `ContainerLifetime.Persistent` anywhere | Remove entirely. Ephemeral-always is the team rule. |
| Two AppHosts (one "real", one "for tests") with different resources | Collapse to one AppHost. Tests mutate / subscribe to events. |
| Dedicated `Seeder` console project run before tests | Delete. Replace with `[ClassInitialize]` invoking one of the four strategies. |
| `if (tests-ish) Add<FakeEmailSender>` in AppHost | Not a seed issue — it's a stub. Wire as a real container resource. |
| Hardcoded connection string in the seed file | Replace with `await _app.GetConnectionStringAsync("<name>")`. |
| Conditional-on-`IsDevelopment` seed | Remove. Dev seeding belongs to a developer-run script, not the AppHost. |

## File-splitting rules

- Inline seed in `[ClassInitialize]` only if ≤ ~5 lines and ≤ 1 concern.
- Anything bigger → dedicated file under `test/[Company].[Product]/[Company].[Product].Test/Seeding/`.
- One file per concern: `SqlSeedData.cs`, `ProductCatalogSeedData.cs`, `CosmosSeedData.cs`. Not one giant `Seeds.cs`.
- `internal static class`, `internal static async Task ApplyAsync(...)` entry point. Inputs are connection strings or typed SDK clients — no ambient state.
- Idempotent (`IF NOT EXISTS`, `UpsertItemAsync`, `ON CONFLICT DO NOTHING`). Idempotence still matters under ephemeral mode because the per-class fixture may re-enter against the same live container during debugging.

## Cross-references

- [layout.md](layout.md) — `Seeding/` and `TestData/` folder placement, `.csproj` `<None Update="TestData\**" />`.
- [mstest-integration.md](mstest-integration.md) — where in `[ClassInitialize]` the seed call goes.
- [forbidden-patterns.md](forbidden-patterns.md) — companion enforcement of "no seed in production".
- Sibling skill: `dotnet-aspire` § emulators-and-real-infra — where `RunAsEmulator()` and `AsExisting` live on the producer side.
- Live (Aspire seeding guidance): https://learn.microsoft.com/en-us/dotnet/aspire/database/efcore-migrations
- Live (`WithInitFiles` for Postgres): https://learn.microsoft.com/en-us/dotnet/api/aspire.hosting.postgresbuilderextensions.withinitfiles
- Live (Aspire eventing): https://learn.microsoft.com/en-us/dotnet/aspire/app-host/eventing
