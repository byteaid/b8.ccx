# Forbidden Patterns (testing)

Bans that protect the team's "tests run against the real app" invariant. Each entry follows the standard 4-block format: what it looks like → why it's banned → what to do instead → enforcement.

## 1. Third-party mocking libraries in the test project

### What it looks like

```xml
<!-- Acme.Inventory.Test.csproj -->
<PackageReference Include="Moq" Version="..." />
<PackageReference Include="NSubstitute" Version="..." />
<PackageReference Include="FakeItEasy" Version="..." />
<PackageReference Include="WireMock.Net" Version="..." />
```

```csharp
var stripe = new Mock<IStripeClient>();
stripe.Setup(s => s.ChargeAsync(It.IsAny<ChargeRequest>())).ReturnsAsync(new ChargeResult(...));
services.AddSingleton(stripe.Object);
```

### Why it's banned

1. **The whole point of the team's test stack** is that the test exercises the SAME app that ships to production. Wiring fakes in DI defeats it — tests pass against a different object graph.
2. **Mocking libraries hide contract drift.** A `Mock<IStripeClient>` with canned responses keeps passing when the real Stripe SDK changes. The team ships a stub project (a real ASP.NET Core service) so the contract is exercised.
3. **No native emulator, no problem — you stub it.** When a third-party service has no native emulator (Stripe, Twilio, custom partner APIs), the answer is a stub project (`Acme.Inventory.Stubs.Stripe`) that runs as a real Aspire resource. The consumer talks HTTP/gRPC to the stub exactly as it talks to the real vendor.
4. **`TimeProvider` and `Guid.CreateVersion7()` cover the deterministic-time and deterministic-ID problems** without bypass code (see `dotnet-conventions` § csharp-style).

### What to do instead

| Reason you reached for a mock | Correct answer |
|---|---|
| Third-party SDK with no emulator | Stub project. The stub is a real resource in the AppHost; the consumer reads its base URL from configuration like any other Aspire resource. |
| Need to control time | Inject `TimeProvider`; the test uses `FakeTimeProvider` (only inside the test class itself, never in production DI). |
| Need a specific GUID | Inject something that wraps `Guid.CreateVersion7()`; or assert against the response (the test does not need to predict the ID). |
| Want to avoid hitting the database | The database IS up — Aspire boots it ephemerally. Talk to it. |
| External dependency is flaky | It's not. The container or the stub is local. |

### Enforcement

```powershell
# Test project must be clean of mocking libs
gci -Recurse -Filter *.Test.csproj | Select-String "Moq|NSubstitute|FakeItEasy|WireMock"
# Production code must have no Fake* registrations
gci -Recurse src -Include *.cs | Select-String "AddSingleton<.*Fake.*>"
```

Both must return empty. On sight, inside a file you're editing: delete the registration and report which third party needs a stub project.

## 2. Test-specific code paths in production code

### What it looks like

```csharp
// Environment-forked DI
if (env.IsEnvironment("Testing"))
    services.AddDbContext<AppDb>(o => o.UseInMemoryDatabase("test"));
else
    services.AddDbContext<AppDb>(o => o.UseSqlServer(cs));

// Test-only config keys
if (config.GetValue<bool>("Testing:DisableAuth"))
    services.Configure<AuthOptions>(a => a.Disabled = true);

// Fake client wired for "tests"
services.AddSingleton<IEmailSender, FakeEmailSender>();   // with an env check above

// Preprocessor directive
#if INTEGRATION_TEST
builder.Services.AddSingleton<IClock, FrozenClock>();
#endif

// Hosted service that loads fake data
builder.Services.AddHostedService<DbInitializer>();   // body: FakeUsers.Add(...), FakeOrders.Add(...)
```

### Why it's banned

1. **The team's test stack already exercises real infrastructure** (real SQL, Redis, queues via containers; real stubs for third parties). Forking behaviour for "tests" defeats the point — tests pass against a different app than prod runs.
2. **Silent divergence.** A bug that only surfaces in prod because `IsEnvironment("Testing")` swapped a component is one of the worst failure modes the team has lived through.
3. **Invariant of the orchestration:** "the app must behave identically in tests and in production". Any test-only branch violates it.

### What to do instead

| Root cause | Correct answer |
|---|---|
| Seeding data before a test | Seed from the test class — see [seeding.md](seeding.md). |
| Swapping a real dependency for a fake | Register an Aspire stub project via the AppHost — never a DI mock in the consumer. |
| Freezing time | Inject `TimeProvider`; use `FakeTimeProvider` ONLY in the test class, not in production DI. |
| Reference data that ships with the product | Put it in an EF Core migration (`migrationBuilder.InsertData(...)`) — runs identically everywhere. |
| "Auth bothers me in tests" | The test gets a real token from the stub IdP (or a cookie via the real flow). The app stays locked down. |

### Enforcement

- **On sight, inside a file you're editing:** delete the branch and implement the correct answer.
- **On review:** treat as a blocking finding.

## 3. Seed endpoints in application code

### What it looks like

```csharp
app.MapPost("/_test/seed", async (AppDb db) => { /* insert fake rows */ }).RequireHost("...");
app.MapPost("/_test/reset", async (AppDb db) => { /* delete rows */ });
app.MapPost("/api/admin/seed-test-data", async (...) => { ... });

builder.Services.AddHostedService<DbInitializer>();   // FakeUsers/FakeOrders inside

builder.Services.AddDbContext<AppDb>(opts => opts
    .UseSqlServer(cs)
    .UseSeeding((ctx, _) =>
    {
        ctx.Set<User>().Add(new User { Name = "Demo User" });
        ctx.SaveChanges();
    }));
```

### Why it's banned

1. **The app's startup path must be the production startup path.** Branches like `if (Testing) Seed()` mean the app under test is a different app than the app in prod.
2. **Endpoints survive.** `/_test/seed` ships to production once and becomes a forever-attack-surface. The "guarded by `RequireHost`" defence fails the moment hosting changes.
3. **EF Core `UseSeeding` is for reference data, not for tests.** Loading "Demo User" from `UseSeeding` turns the production DB into a test DB at first migration.
4. **Tests own their seed.** The team's per-class fixture seeds in `[ClassInitialize]` against the real connection strings the AppHost provided. The app stays untouched.

### What to do instead

| Source of the seed | Correct home |
|---|---|
| Test-only fake users / orders / catalog rows | Test project, `[ClassInitialize]` invoking one of the four strategies in [seeding.md](seeding.md). |
| Reference / lookup data that ships with the product (countries, currencies, default tenant) | EF Core migration: `migrationBuilder.InsertData(...)`. Runs identically in dev, tests, prod. |
| One-time data backfill for an existing customer | Idempotent migration with a guard (`IF NOT EXISTS`). |

If a banned shape lives in a file you are editing, delete it and report. If the test suite breaks because the seed went away, the test project re-homes it. The app code does not preserve the seed for backwards compatibility.

### Enforcement

```powershell
gci -Recurse src -Include *.cs | Select-String "(_test/(seed|reset)|seed-test-data|DbInitializer|SeedingService)"
```

Must be empty in non-test projects.

## 4. Mocks / fakes wired into consumer DI

### What it looks like

```csharp
// Program.cs of a host project
if (env.IsEnvironment("Testing"))
{
    services.AddSingleton<IEmailSender, FakeEmailSender>();
    services.AddSingleton<IStripeClient, FakeStripeClient>();
}
```

### Why it's banned

Same root as § 1 and § 2: the production composition root must contain only real components. Testing concerns belong on the **test side** (stub projects, real Aspire resources, real SDK clients). The consumer host knows nothing about tests.

### What to do instead

- Replace the fake with a stub project registered in the AppHost.
- The consumer reads the stub's base URL via configuration like any other resource.
- The test class drives the stub through its real HTTP/gRPC surface.

### Enforcement

```powershell
gci -Recurse src -Include *.cs | Select-String "AddSingleton<.*Fake.*>|Mock<"
```

Empty in `src/`.

## 5. Hand-rolled fakes inside the test project

### What it looks like

```csharp
// Inside Company.Product.Test — no Moq anywhere, but:
private sealed class EmbeddedTemplateStore : INotificationTemplateStore
{
    public Task<string> GetBodyAsync(string key, CancellationToken ct) =>
        Task.FromResult(ReadEmbeddedResource(key));   // canned answer
}

private sealed class NoOpSharedAssetStore : INotificationSharedAssetStore { ... }

// Or: an in-process listener standing in for a real downstream
var sink = new HttpListener();          // "recording webhook sink" living in the test process
sink.Prefixes.Add("https://127.0.0.1:5599/");
```

### Why it's banned

1. **A class in the test project implementing a production port IS a mock** — the absence of `Moq` in the `.csproj` changes nothing. The component under test runs against a hand-written canned object graph, not the one that ships.
2. **It evades the greppable enforcement** (§ 1 only catches packages), which is precisely why it accumulates: each instance looks small and "pragmatic".
3. **In-process sinks are stub projects that refused to be one.** An `HttpListener` inside the test process is invisible to the AppHost topology, unlogged by `Blaztrap.Aspire.FileLogging`, and unusable from `aspire run`.

### What to do instead

| Reason you reached for the fake | Correct answer |
|---|---|
| Component needs data a store serves | Seed the REAL store (blob container, DB) via [seeding.md](seeding.md) and drive the component through its real surface. |
| Need to capture an outbound call (webhook, mail, partner API) | Stub **project** registered in the AppHost; it records to a file under the artefact root and the test reads that file. |
| The behaviour is "too internal" to reach through a surface | That is a missing real surface or a mis-modelled flow — escalate to `dotnet-architect` / `analyst`. Do not test the internals directly. |

`FakeTimeProvider` inside the test class remains the ONE sanctioned in-test double (deterministic time; see § 1).

### Enforcement

```powershell
# Test classes implementing production-looking ports — review every hit
gci -Recurse test -Include *.cs | Select-String "(class|record)\s+\w+\s*:\s*I[A-Z]\w+(Store|Repository|Client|Sender|Service|Provider|Publisher)"
# In-process sinks
gci -Recurse test -Include *.cs | Select-String "new HttpListener|WebApplication.Create"
```

Both must come back empty (or only `FakeTimeProvider` usages).

## 6. In-process "guard" / composition / parity tests

### What it looks like

```csharp
// Folders that should not exist: Hosting/, Authorization/, Notifications/, Permissions/ ...
[TestClass]
public class WebHostNotificationDiValidationTests   // builds the host, asserts DI graph
[TestClass]
public class AuthenticationStateClaimsRoundTripTests // serializer round-trip, no surface
[TestClass]
public class SlimDomainEventRichInterfaceParityTests // reflection parity check
// usually decorated with: // INTENTIONAL-ORPHAN: cross-cutting guard
```

### Why it's banned

1. **They are unit tests under a euphemism.** "Guard", "composition check", "parity test", "contract test" — none reaches the system through an executable surface, so none proves the shipped behaviour.
2. **The orphan marker is not an exemption from the hard rules.** `// INTENTIONAL-ORPHAN` exists for a real-surface test that has no flow (an environment smoke test) — it never converts an in-process test into a legal one.
3. **They breed folders outside the surface taxonomy** (`Hosting/`, `Authorization/`, …), and each new folder normalises the next violation.
4. **The failure they guard against is reachable through a real surface.** A broken DI registration fails the resource's health check at mount; a broken serializer breaks the UI flow that round-trips the claim; a parity break surfaces in the queue flow that consumes the event.

### What to do instead

| The guard's intent | Correct answer |
|---|---|
| "App must start with a valid DI graph" | Already covered: every fixture mount waits for the resource to be `Healthy`. A DI failure fails every test in the class with `apphost.log` pointing at the cause. |
| "Serialized X must round-trip" | The UI / HTTP / queue flow that carries X asserts the observable result. |
| "Template must render all fields" | Drive the flow that sends the message; assert the captured payload from the stub project. |
| "Two interfaces must stay in sync" | An analyzer or a compile-time check in the production solution — not a test. |

When the migration target is unclear, delete the guard and record the gap as a `BG-NNN` — a misleading green check is worse than a visible gap.

### Enforcement

Any `[TestClass]` that (a) does not inherit `AppHostFixture` and (b) is not a pure client of an external surface (CLI child process) is a finding. Any folder outside the derived surface set ([layout.md](layout.md) § Surface folders) is a finding.

## 7. Legacy / dispersed log capture

### What it looks like

```xml
<PackageReference Include="Blaztrap.Aspire.Testing.FileLogging" Version="0.1.1" />
```

```csharp
appBuilder.AddResourceFileLogging(LogsDir);                     // legacy API — no apphost.log
var dir = Environment.GetEnvironmentVariable("BLAZTRAP_TEST_RUN_DIR")
       ?? Environment.GetEnvironmentVariable("MYAPP_RESOURCE_LOG_DIR");
LogsDir = Path.Combine(repoRoot, "tests", "automated", className); // in-repo artefacts
File.WriteAllText(".browser-session.json", state);               // repo-root session file
```

### Why it's banned

1. **The legacy package drops `apphost.log`** — Aspire / DCP / dashboard output, which is where every "resource failed to start" diagnosis lives. A suite on the legacy package debugs blind.
2. **In-repo artefact folders disperse the evidence.** Logs in `tests/automated/`, recordings somewhere else, trx under `TestResults/` — no single root to upload, archive, or post-mortem; agents end up "manually collecting" logs.
3. **Env-var path overrides fork the truth.** `TestContext` already knows the run folder; a parallel env-var convention guarantees the two diverge.

### What to do instead

- `Blaztrap.Aspire.FileLogging` ≥ 0.1.0, `appHost.AddFileLogging(...)` — one call in `AppHostFixture`.
- Every path derives from `TestArtifacts.RunDir(context)` / `TestArtifacts.AuthDir(context)` ([mstest-integration.md](mstest-integration.md) § Test artefacts).
- Auth state at `TestResults/.auth/{role}-state.json`, generated **unattended** — a test that needs a human to complete sign-in (headed browser + manual SSO) is itself a finding; see `playwright-dotnet` § auth-storage.

### Enforcement

```powershell
gci -Recurse -Filter *.csproj | Select-String "Blaztrap.Aspire.Testing.FileLogging"
gci -Recurse test -Include *.cs | Select-String "AddResourceFileLogging|BLAZTRAP_TEST_RUN_DIR|_LOG_DIR|tests[/\\]automated"
```

Both must come back empty.

## 8. Editing tests outside the testing scope

### What it looks like

A non-developer surface (planner, infra, Azure operator, …) modifying a test file or running `dotnet test` ad-hoc against `[Company].[Product].Test`.

### Why it matters

- **Tests own the spec.** When TDD is the methodology in flight (`development-methodology` § tdd), the dev's job is to make the RED test GREEN. Rewriting "their" failing test under deadline pressure is the classic anti-pattern.
- **Single owner per surface.** The single test project has one owner; multiple authors muddy traceability — who wrote the assertion, why, and against which acceptance criterion.

### What to do instead

- Read tests for context — that is encouraged. **Editing them is the line.**
- If a test seems wrong: report. Examples:
  - "Test `Orders_Tests.CreateOrder_ReturnsCreated` asserts `HttpStatusCode.Created`, but the architecture mandates `Accepted` because the operation is async — flagging for revision."
  - "The test references `IOrderService.CreateAsync` with three parameters; the architecture defines two — flagging for clarification."
- The test author revises; the dev re-runs the implementation against the revised spec.

### Enforcement

- **`dotnet new mstest` / `xunit` / `nunit` are forbidden** — the project already has its single `*.Test.csproj`. See [layout.md](layout.md).
- **PRs that mix application code changes with non-trivial test changes** are reviewed for ownership.

## See also

- [layout.md](layout.md) — single test project rule and folder layout that these bans protect.
- [seeding.md](seeding.md) — the legitimate home for everything § 2 / § 3 try to put in production.
- [mstest-integration.md](mstest-integration.md) — per-class mount and parallelism settings.
- Sibling skill: `dotnet-conventions` § build-quality/clean-as-you-touch — the scope-bounded eradication policy that turns these bans into action when you encounter them.
- Sibling skill: `dotnet-conventions` § csharp-style/time-provider, csharp-style/guid-createversion7 — first-party answers to the determinism problems people reach for mocks to solve.
