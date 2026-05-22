# Project naming — `[Company].[Product][.{Module}]`

> Authoritative source: `dotnet-hexagonal-architecture` § solution-layout § Naming rules.

## Rule

Every project name follows the pattern:

```
[Company].[Product][.{Module}]
```

- `[Company]` — fixed per organization (e.g., `Acme`, `Contoso`, `ByteAid`).
- `[Product]` — fixed per product / solution (e.g., `Inventory`, `GymTracker`, `BillingSuite`).
- `{Module}` — optional sub-discriminator. The Core (application) project drops it. Concrete adapters use the technology name directly. Hosts use the surface name.

The matching `.csproj` filename and root namespace track the project name verbatim.

## Examples

| Concern | Project name |
|---|---|
| Application (Core) | `Acme.Inventory` |
| Ports (data only) | `Acme.Inventory.Interface` |
| Domain models | `Acme.Inventory.Models` |
| Enums / constants (incl. `ErrorCode`) | `Acme.Inventory.Constants` |
| Infrastructure abstractions | `Acme.Inventory.Infrastructure` |
| SQL Server adapter | `Acme.Inventory.SqlServer` |
| Cosmos DB adapter | `Acme.Inventory.Cosmos` |
| Redis cache adapter | `Acme.Inventory.Redis` |
| Azure Blob storage adapter | `Acme.Inventory.AzureStorage` |
| Web API host | `Acme.Inventory.WebAPI` |
| Worker host | `Acme.Inventory.Worker` |
| gRPC server host (carries the `.proto` files) | `Acme.Inventory.gRPC.Server` |
| Cli host | `Acme.Inventory.Cli` |
| Blazor host | `Acme.Inventory.Web` |
| MAUI host | `Acme.Inventory.Mobile` |
| Desktop host | `Acme.Inventory.Desktop` |
| Aspire orchestrator | `Acme.Inventory.AppHost` |
| Aspire defaults | `Acme.Inventory.ServiceDefaults` |
| Single test project (singular `Test`) | `Acme.Inventory.Test` |
| External-vendor stub host | `Acme.Inventory.Stubs.Stripe` |

## Banned shapes

- `Acme.Inventory.Application` / `Acme.Inventory.Domain` — Core is just `Acme.Inventory`.
- `Acme.Inventory.Backend` — `Backend` is not a layer.
- `Acme.Inventory.Common` / `Acme.Inventory.Shared` — vague containers; pick a specific layer.
- `Acme.Inventory.Tests` — singular `Test`, never plural.
- `Acme.Inventory.UnitTests` / `Acme.Inventory.IntegrationTests` / `Acme.Inventory.E2ETests` — there is **one** test project: `Acme.Inventory.Test`. See [single-test-project-rule.md](single-test-project-rule.md).
- `Acme.Inventory.Data.SqlServer` / `.Data.Cosmos` / `.Data.Redis` / `.Data.Blob` — adapters use the technology name directly: `Acme.Inventory.SqlServer`, `Acme.Inventory.Cosmos`, etc.
- Lowercase project names — PascalCase always.

## Enforcement

- **Architecture review:** new projects must match the pattern; deviations are flagged.
- **Code review:** namespace must match the project name exactly. `<RootNamespace>` overrides are not allowed without justification.
- **Clean-as-you-touch:** project renames are coordinated refactors — surface as a TODO; do not rename unilaterally.
