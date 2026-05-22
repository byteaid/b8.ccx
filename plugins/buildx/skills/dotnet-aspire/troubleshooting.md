# Troubleshooting Catalogue

Symptom → cause → fix → source. Grouped by phase. When you hit the same error twice without progress, escalate per the team rule "3 attempts then search" (issues, discussions, official docs).

## A. Boot / health

### A1. Resource stuck in `Starting`

**Symptom:** dashboard shows a resource in `Starting` forever; consumers with `WaitFor(resource)` never start. No container logs.

**Causes:**
- Docker / Podman not running, or user lacks permissions.
- An `EnvironmentCallbackAnnotation` or `ConnectionStringAvailableEvent` handler awaits something that never arrives.
- `.WithHttpHealthCheck("/health")` points to a path that doesn't exist in the container.
- `WaitFor` against a pure `ConnectionStringResource` — there's nothing to detect Healthy on.

**Fix:**
- `docker info` first. If it fails, start Docker.
- Make AppHost callbacks `async Task` without sync blocks (no `.Result` / `.Wait()`).
- Verify the health-check path is real.
- Don't `WaitFor` a `ConnectionStringResource`; only wait on resources with health checks.

**Source:** https://github.com/dotnet/aspire/issues/6613, https://github.com/dotnet/aspire/issues/6858

### A2. SQL Server: pre-login handshake error

**Symptom:** `Microsoft.Data.SqlClient.SqlException: ... pre-login handshake (provider: SSL Provider, error: 31)` or "connection aborted".

**Causes:**
- Container SA password fails policy → engine never starts. Logs show "Password validation failed".
- macOS / ARM: container loses port mapping when AppHost restarts.
- SqlClient 4+ enforces TLS against self-signed container cert.

**Fix:**
- Pass `builder.AddParameter("sa-pwd", secret: true)` with ≥8 chars including uppercase + digit + symbol, or let Aspire generate one (default).
- On macOS, stop containers between sessions. Do **not** work around with `ContainerLifetime.Persistent` — see [test-seeding.md](test-seeding.md).
- Add `TrustServerCertificate=True;Encrypt=False` to consumer CS, or use `AddAzureSqlServer("sql").RunAsContainer()` — Aspire 13.x adds the flags automatically.

**Source:** https://github.com/dotnet/aspire/issues/1023, https://github.com/dotnet/aspire/issues/1168, https://github.com/dotnet/aspire/issues/12056

### A3. Redis / RabbitMQ / Kafka don't reach Healthy

**Symptom:** consumer with `WaitFor(broker)` takes minutes or fails with "Resource failed to become healthy". Kafka especially — leader election + internal topic creation is slow.

**Cause:** default health check is strict / short-timeout.

**Fix:**
- Increase the publisher timeout:
  ```csharp
  builder.Services.Configure<HealthCheckPublisherOptions>(o =>
  {
      o.Period  = TimeSpan.FromSeconds(2);
      o.Timeout = TimeSpan.FromSeconds(10);
  });
  ```
- For Kafka, prefer `confluentinc/cp-kafka` and run a `kafka-topics --bootstrap-server` probe before marking Healthy.
- Accept the cold start. `ContainerLifetime.Persistent` is forbidden under the team's ephemeral-always rule. Mitigate by keeping topics minimal and stubbing Kafka where the broker isn't under test.

**Source:** https://github.com/dotnet/aspire/issues/5645

### A4. Health check flickers 503 → dashboard ping-pongs

**Symptom:** Healthy/Unhealthy oscillates; consumers retry-loop.

**Cause:** the service returns 503 from a transient dependency check (DB reconnect, Redis failover) under default 1-second polling without hysteresis.

**Fix:** split `/health/live` (process self-check) from `/health/ready` (dependencies). Use `live` for AppHost startup gating:

```csharp
// Consumer Program.cs
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"])
    .AddDbContextCheck<AppDbContext>("db", tags: ["ready"]);

app.MapHealthChecks("/health/live",  new() { Predicate = r => r.Tags.Contains("live") });
app.MapHealthChecks("/health/ready", new() { Predicate = r => r.Tags.Contains("ready") });

// AppHost
builder.AddProject<Projects.Api>("api").WithHttpHealthCheck("/health/live");
```

**Source:** https://learn.microsoft.com/en-us/dotnet/aspire/fundamentals/health-checks, https://github.com/dotnet/aspire/issues/5569

### A5. `WaitFor` ignored — consumer starts before dependency

**Symptom:** API starts, fails connecting to DB, crashes; second attempt works.

**Cause (extremely common):** the dependency has **no registered health check**, so `WaitFor` only waits for `Running` (process alive) — effectively a no-op for custom containers.

**Fix:** call `.WithHttpHealthCheck(...)` or `.WithHealthCheck("name")` on the dependency.

```csharp
// BAD — no health check, WaitFor only waits for Running
var stub = builder.AddContainer("stub", "myorg/stub:latest");
var api  = builder.AddProject<Projects.Api>("api").WaitFor(stub);

// GOOD
var stub = builder.AddContainer("stub", "myorg/stub:latest")
    .WithHttpHealthCheck("/health");
var api  = builder.AddProject<Projects.Api>("api").WithReference(stub).WaitFor(stub);
```

Aspire 13.1+ has `.WaitForHealthy(dep)` that fails loud if no health check is registered, instead of silently degrading.

**Source:** https://github.com/dotnet/aspire/issues/5645

## B. Connection strings / service discovery

### B1. Consumer doesn't see the connection string

**Symptom:** `builder.Configuration.GetConnectionString("db")` returns `null`.

**Cause:** missing `WithReference(db)` on that specific consumer. Aspire only injects env vars into resources that declare the dependency.

**Fix:**
```csharp
builder.AddProject<Projects.Api>("api")
    .WithReference(db)     // missing
    .WaitFor(db);
```

Verify on the dashboard's Environment tab (visible since 13.x) or via `aspire run --dry-run`.

**Source:** https://github.com/dotnet/aspire/discussions/5226

### B2. Service discovery name doesn't resolve

**Symptom:** `HttpClient` with `BaseAddress = new Uri("http://api")` throws `SocketException: No such host is known`.

**Causes:**
- Missing `AddServiceDiscovery()` / `ConfigureHttpClientDefaults(b => b.AddServiceDiscovery())` (normally part of `AddServiceDefaults()`).
- Missing `WithReference(api)` in AppHost → no `services__api__http__0=...` env vars.
- `HttpClient` instantiated with `new HttpClient()` instead of `IHttpClientFactory` — bypasses the entire pipeline.

**Fix:**
```csharp
// ServiceDefaults
builder.Services.AddServiceDiscovery();
builder.Services.ConfigureHttpClientDefaults(b => b.AddServiceDiscovery());

// AppHost
api.WithReference(downstreamApi);

// Consumer — always via IHttpClientFactory
builder.Services.AddHttpClient<IDownstreamClient, DownstreamClient>(c =>
    c.BaseAddress = new Uri("http://downstreamapi"));
```

**Source:** https://github.com/dotnet/aspire/issues/6864

### B3. Resource-name mismatch in `GetConnectionString`

**Symptom:** `GetConnectionString("postgres")` returns null even though the resource exists — consumer is asking for "pg".

**Cause:** the injected key is exactly the **most specific** resource name — `AddDatabase("appdb")` produces `appdb`, not the parent `pg`.

**Fix:** align the names, or rename via `WithReference(db, connectionName: "postgres")`:

```csharp
var pg = builder.AddPostgres("pg");
var db = pg.AddDatabase("appdb");
var api = builder.AddProject<Projects.Api>("api").WithReference(db);

// Consumer reads "appdb", NOT "pg".
var cs = builder.Configuration.GetConnectionString("appdb");
```

**Source:** https://github.com/dotnet/aspire/discussions/2437

### B4. Container reachable from host but not from another container

**Symptom:** API in `aspire run` works; same API as a container fails with "connection refused".

**Cause:** Aspire publishes ports to the host, but containers can't talk to the host's `localhost`. Hardcoding `localhost:5432` breaks the multi-container case.

**Fix:** never hardcode. Read `ConnectionStrings__xxx` from configuration — Aspire emits the right address depending on consumer context (host vs container; `host.docker.internal:port` or the Docker network name as appropriate).

**Source:** https://github.com/dotnet/aspire/issues/8286

### B5. Hardcoded port breaks parallelism

**Symptom:** two AppHosts (dev + test, or two devs on the same machine) collide: "port already in use".

**Cause:** `WithEndpoint(port: 5432, targetPort: 5432)` with a fixed host port. Aspire by default assigns a free dynamic host port and proxies it.

**Fix:** omit `port:` (let Aspire choose); read the URL from the env var. If a fixed port is unavoidable (external debug tool), restrict to known single-instance scenarios. To run two AppHosts in parallel: `aspire start --isolated`.

**Source:** https://github.com/dotnet/aspire/issues/10146

## C. Tests

### C1. Tests flake because resources aren't ready

**Symptom:** test queries the DB and fails "connection refused" / "database does not exist". Second attempt passes.

**Cause:** `BuildAsync().StartAsync()` returns when the **AppHost process** is up — not when resources are Healthy.

**Fix:** after `StartAsync`, wait per resource:

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(180));
await Task.WhenAll(
    App.ResourceNotifications.WaitForResourceHealthyAsync("db",  cts.Token),
    App.ResourceNotifications.WaitForResourceHealthyAsync("api", cts.Token),
    App.ResourceNotifications.WaitForResourceHealthyAsync("web", cts.Token));
```

If the resource has no registered health check, the wait completes on `Running`, which is not "ready". Register health checks. See [integration-testing.md](integration-testing.md) § 3.

**Source:** https://github.com/dotnet/aspire/issues/13714

### C2. Containers reuse state between test runs

**Symptom:** first run passes; second fails because data is already there.

**Cause:** someone left `WithLifetime(ContainerLifetime.Persistent)` or `WithDataVolume("name")` in the AppHost. Both forbidden under the ephemeral-always rule — see [test-seeding.md](test-seeding.md).

**Fix:** remove the calls entirely from the AppHost. Don't wrap them in `IsRunMode`. Every boot starts from a blank container. If the test needs seeded data, use one of the four canonical strategies in [test-seeding.md](test-seeding.md).

**Source:** https://github.com/dotnet/aspire/issues/6850, https://github.com/dotnet/aspire/issues/6888

### C3. Test host pays cold-start every run

**Symptom:** CI takes minutes per run; local re-runs too.

**Cause:** `DistributedApplicationTestingBuilder` adds a random suffix to resource names for isolation — prevents persistent-container reuse (by design, not a bug).

**Fix (workarounds, no official remedy as of 2026-04):**
- Pay the cold start once per **assembly** via `[ClassInitialize(InheritanceBehavior.BeforeEachDerivedClass)]` + `[AssemblyCleanup]`, not per class — see [integration-testing.md](integration-testing.md) § 4.
- For unit-only paths that don't need full Aspire orchestration, use `Testcontainers.*` directly.
- Pre-create containers outside Aspire and wire them via `AddConnectionString(...)` in a dedicated test AppHost (last resort).

Open: https://github.com/dotnet/aspire/issues/6891, https://github.com/dotnet/aspire/issues/10888

### C4. `Persistent` lifetime doesn't actually persist in tests

**Symptom:** `WithLifetime(ContainerLifetime.Persistent)` creates a new container per run.

**Cause:** the testing builder runs in a randomised namespace (see C3). "Same" resource across two runs is two distinct containers — intentional, to isolate parallel tests.

**Fix:** under the ephemeral-always rule the call shouldn't be there in the first place. Remove it. If cold-start cost hurts, amortise via `[AssemblyInitialize]` + Docker layer cache.

**Source:** https://github.com/dotnet/aspire/issues/6850

### C5. `DistributedApplicationTestingBuilder` can't find the AppHost

**Symptom:** `CreateAsync<Projects.AppHost>()` throws `FileNotFoundException` or "Type 'Projects.AppHost' not found".

**Causes:**
- Missing `ProjectReference` from test project to AppHost.
- AppHost lacks `<IsAspireHost>true</IsAspireHost>` — without it, the source generator doesn't emit `Projects.*`.
- Different `TargetFramework` between projects.
- Missing `Aspire.Hosting.Testing` package.

**Fix:**
```xml
<!-- Test csproj -->
<PackageReference Include="Aspire.Hosting.Testing" Version="13.2.*" />
<ProjectReference Include="..\AppHost\AppHost.csproj" />

<!-- AppHost csproj -->
<TargetFramework>net10.0</TargetFramework>
<IsAspireHost>true</IsAspireHost>
```

**Source:** https://github.com/dotnet/aspire/issues/7008

## D. Dashboard / telemetry / runtime

### D1. Dashboard shows no traces

**Symptom:** Traces tab is empty even though the service is running.

**Causes:**
- The consumer doesn't include `Aspire.ServiceDefaults` or doesn't call `builder.AddServiceDefaults()` — no OTel exporters registered.
- `OTEL_EXPORTER_OTLP_ENDPOINT` not injected because the resource isn't `WithReference`-d.
- .NET Framework 4.8: default OTLP/gRPC isn't supported.

**Fix:**
- `builder.AddServiceDefaults()` must be the first line of the host's `Program.cs`.
- For .NET Framework consumers, set `OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf`.

**Source:** https://learn.microsoft.com/en-us/dotnet/aspire/fundamentals/telemetry

### D2. OTel exporter sends 0 spans

**Symptom:** silent logs, empty dashboard, app running.

**Causes:**
- Exporter targets `localhost:18889` from inside a container — `localhost` isn't the host.
- Self-signed dashboard cert rejected by the exporter.

**Fix:** Aspire injects the right OTLP env vars automatically. If you override `OTEL_EXPORTER_OTLP_ENDPOINT` in `appsettings.json`, **remove the override**. For containers, Aspire uses `host.docker.internal` or the Docker-network name automatically.

**Source:** https://learn.microsoft.com/en-us/dotnet/aspire/fundamentals/telemetry

### D3. Custom container's logs missing from dashboard

**Symptom:** "No logs" tab on a custom container that is clearly writing.

**Cause:** the process writes to a file, or the runtime buffers output (Python, .NET buffered, Windows console).

**Fix:** force stdout/stderr writes:

| Runtime | Setting |
|---|---|
| Python | `PYTHONUNBUFFERED=1` env var |
| Node | `console.log` / `console.error` directly |
| Windows exe | `WithArgs("--console-log")` or equivalent flag |
| .NET | `Console.WriteLine` is unbuffered — usually fine |

**Source:** https://learn.microsoft.com/en-us/dotnet/aspire/fundamentals/telemetry

## E. Publish / deploy

### E1. `azd up` fails with manifest error

**Symptom:** `generating bicep from manifest: argument 1 cannot contain connection strings, secured parameters, or secret outputs. Use environment variables instead`, or "directory not found" when looking for generated Dockerfiles.

**Causes:**
- A secret CS passed as a positional argument to a resource — Bicep can't literalise secrets.
- `azd` deletes `obj/aspire-manifest-*` after reading; Aspire 13 may have placed dynamic Dockerfiles there, breaking the build step.

**Fix:**
- Move the secret to `WithEnvironment("KEY", param)` with `AddParameter("key", secret: true)`.
- Update `azd` to a version that doesn't delete the directory, or use `PublishAsDockerfile` against a checked-in Dockerfile **outside `obj/`**. See [publish-deploy.md](publish-deploy.md).

**Source:** https://github.com/dotnet/aspire/issues/7429, https://github.com/Azure/azure-dev/issues/5911

### E2. ACA doesn't pick up expected env vars

**Symptom:** app starts in Container Apps but `GetConnectionString` returns null or stale value.

**Causes:**
- Manifest emitted the env var with a `ConnectionStringExpression` that resolved against local (dev) values.
- `azd` didn't map the dependency to the target ACA correctly.
- Env vars edited in the portal — overwritten on next `azd deploy`.

**Fix:**
- Move sensitive config to parameters with `secret: true` — they materialise as Container App secrets.
- Manage env vars **only** via the AppHost; never via the portal.
- For dynamic CS in ACA, use `WithEnvironment("CS", resource.ConnectionStringExpression)` explicitly. See [publish-deploy.md](publish-deploy.md).

**Source:** https://github.com/dotnet/aspire/issues/11408

## Quick diagnostic checklist

When something breaks, in shortcut order:

1. **Is Docker running?** `docker info`. 80% of "stuck in Starting".
2. **Is `AddServiceDefaults()` the first line of `Program.cs`?** Without it: no telemetry, no service discovery.
3. **Is `MapDefaultEndpoints()` before `MapControllers()` / `MapGroup()`?** Wrong order → `/health` doesn't exist.
4. **Does the CS name match?** `AddDatabase("appdb")` → `GetConnectionString("appdb")`. Not "sql", not "postgres".
5. **Is `WithReference(dep)` declared on every consumer that needs it?** Without it: no env vars.
6. **Does the dependency expose a registered health check?** Without one, `WaitFor` is almost a no-op. Use `WithHttpHealthCheck` or `WaitForHealthy` (13.1+).
7. **Hardcoded ports anywhere?** Remove `port:` — always dynamic.
8. **Secrets via `AddParameter(..., secret: true)`?** Not plain text. Not appsettings. Not hardcoded.
9. **Tests waiting on `Healthy` (not `Running`)?** `WaitForResourceHealthyAsync` with a 3-minute budget for cloud cold starts.
10. **Dashboard's Environment tab — does the injected value match expectation?**

## Cross-references

- Live (Aspire troubleshooting): https://learn.microsoft.com/en-us/dotnet/aspire/troubleshooting/overview
- Live (health checks): https://learn.microsoft.com/en-us/dotnet/aspire/fundamentals/health-checks
- Aspire issues: https://github.com/dotnet/aspire/issues
- Sibling: [integration-testing.md](integration-testing.md), [test-seeding.md](test-seeding.md), [publish-deploy.md](publish-deploy.md)
