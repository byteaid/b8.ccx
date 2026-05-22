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

## 5. Editing tests outside the testing scope

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
