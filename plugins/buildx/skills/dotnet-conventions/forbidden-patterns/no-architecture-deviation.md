# Forbidden — Deviating from the official architecture (hexagonal)

Rule slug: `architecture-deviation-hexagonal`.

The team's canonical architecture is **hexagonal (ports-and-adapters)** as described in `dotnet-hexagonal-architecture`. Every new project starts from that layout; every existing project either already follows it or has a recorded `structural` debt row stating it does not. A slice that adds code in a way that violates the hexagonal invariants (Infrastructure referencing Interface; Application calling concrete adapters; types in the wrong project) is a deviation and must be flagged.

## What it looks like

```csharp
// Banned — Infrastructure project references the Interface project
//   src/Acme.Product/Acme.Product.Infrastructure/Acme.Product.Infrastructure.csproj
//   <ProjectReference Include="..\Acme.Product.Interface\Acme.Product.Interface.csproj" /> ← banned
// (the dependency-flow invariant says Infrastructure has NO knowledge of Interface)

// Banned — Application service calls a concrete adapter directly
public sealed class CancelOrderHandler(SqlOrderRepository repo)   // banned — concrete adapter
{
    // The application layer must depend on the port (IOrderRepository), not the adapter
    // (SqlOrderRepository). The adapter is registered to the port in the composition root.
}

// Banned — a type lives in the wrong project
// File: src/Acme.Product/Acme.Product/Domain/OrderDto.cs  ← banned (DTO in the Core project)
// DTOs live in `Acme.Product.Models`; Core only contains domain types and ports.

// Banned — host project authors business logic
// File: src/Acme.Product/Acme.Product.AppHost/Workflows/CancelOrderWorkflow.cs ← banned
// AppHost composes the system; it does not implement business logic.

// Banned — adapter raises a domain event directly
public sealed class SqlOrderRepository(IEventBroker events) : IOrderRepository
{
    public async Task Save(Order order)
    {
        // ...
        await events.Raise(new OrderSavedEvent(order.Id)); ← banned — events flow from Application
    }
}
```

## Why it's banned

1. **The dependency-flow invariant is what makes the architecture testable.** Application talks to ports; ports are interfaces in `Core`; adapters in `Infrastructure` implement the ports. A reverse dependency (`Infrastructure` → `Interface`, or `Application` → concrete adapter) collapses the boundary that lets the team swap an adapter without touching application code.
2. **Drift compounds.** One handler that takes a concrete `SqlOrderRepository` is fixable in five minutes; ten handlers that do the same thing become a refactor weekend that nobody schedules.
3. **The project layout encodes the architecture.** Types in the wrong project make the layout meaningless — the next developer reads `Acme.Product` and finds DTOs, then concludes "DTOs go here", and the drift spreads.
4. **The reviewer cannot catch a deviation it cannot find.** If hexagonal invariants are not consistently applied, every review becomes a judgement call instead of a check against a known shape.

## What to do instead

The full canonical shape is owned by `dotnet-hexagonal-architecture`. The short version:

```
src/[Company].[Product]/
├── [Company].[Product]/                     # Core — domain types, ports (interfaces), Result/Command/Event bases
├── [Company].[Product].Interface/           # Public surface (e.g., gRPC contracts, public DTOs exposed to other apps)
├── [Company].[Product].Models/              # DTOs (request/response/internal mapping shapes)
├── [Company].[Product].Constants/           # Constants, enums (notably ErrorCode)
├── [Company].[Product].Infrastructure/      # Application services (the orchestrators) — references Core, never Interface
├── [Company].[Product].{TechAdapter}/       # Tech-named adapters (e.g., .Sql, .ServiceBus, .Smtp) implementing Core ports
├── [Company].[Product].AppHost/             # Aspire composition root
└── [Company].[Product].{Host}/              # Public-facing host (Web, Worker, Cli)
```

**Dependency-flow invariants:**

- Core has zero project references.
- Interface depends only on Core (and Models if it needs to expose DTOs).
- Models depends only on Core.
- Infrastructure depends on Core, Models, Constants. **It does NOT depend on Interface.**
- Tech adapters depend on Core (for the port they implement), Models, Constants.
- Hosts depend on Infrastructure + the adapters they want wired (composition root).
- AppHost depends on the Aspire model and references no source projects directly.

**Application-service rule:** every command/query handler in `Infrastructure` depends on ports from `Core`, never on concrete adapters. The composition root maps each port to one concrete adapter.

**Events rule:** domain events are raised from `Infrastructure` application services only, via the delegate-first event broker. Adapters never raise events.

## The aptness rule

Slice-scope vs project-scope, as with other rules:

- **Project IS hexagonal:** the rule applies in full. A new file in the wrong project, a new handler taking a concrete adapter, an Infrastructure → Interface reference — each is a `major` slice-scope deviation. Clean-as-you-touch removes existing offenders the slice already touches.
- **Project is partly or wholly non-hexagonal AND user has NOT requested a migration:** the reviewer does NOT auto-restructure. A single `structural` row in `debt.md` records "project deviates from hexagonal — {scope}; not slated for migration" (severity `structural`, status `accepted`, owner `(no one)`). New deviations *added by the current slice* are still flagged with their own `major` rows; pre-existing deviations the slice does not touch are subsumed under the `structural` row.

See `development-documentation` § debt § "The aptness rule".

## Greenfield (blank) default

A brand-new .NET solution is bootstrapped in hexagonal layout by default, per `dotnet-hexagonal-architecture`. Confirm the `[Company].[Product]` root with the user, then scaffold:

- `[Company].[Product]` (Core)
- `[Company].[Product].Interface`
- `[Company].[Product].Models`
- `[Company].[Product].Constants`
- `[Company].[Product].Infrastructure`
- `[Company].[Product].AppHost`
- the host(s) the product needs

…and wire them into the `.slnx`.

## Enforcement

- **Developer (`dotnet-developer`)** places every new type in the right project; the handback report names the project per file changed.
- **Reviewer (`dotnet-reviewer`)** on every dispatch:
  - Reads the project's `.slnx` and the `.csproj` graph to confirm the dependency-flow invariants hold for every project the slice touched.
  - Greps each changed file for constructor parameters / fields that name a concrete adapter type instead of a port (`SqlOrderRepository repo` vs `IOrderRepository repo`).
  - Lists deviations with a recommended placement in the hand-off; writes one debt row per deviation per § "The aptness rule".
- **Clean-as-you-touch:** while editing a file that already deviates, the developer corrects the placement / dependency in the same change (per [../build-quality/clean-as-you-touch.md](../build-quality/clean-as-you-touch.md)).

## See also

- `dotnet-hexagonal-architecture` — the authoritative description of the architecture this rule defends.
- [../project-layout/index.md](../project-layout/index.md) — the project-name and folder layout that materialises hexagonal.
- [../build-quality/clean-as-you-touch.md](../build-quality/clean-as-you-touch.md) — scope-bounded eradication policy.
- `development-documentation` § debt — debt-row shape, severity, aptness rule.
