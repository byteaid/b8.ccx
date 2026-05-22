# Hexagonal layer table

> Authoritative source: `dotnet-hexagonal-architecture`. This leaf is a tight summary; the hexagonal skill wins on any disagreement.

## Rule

Every greenfield solution maps onto the canonical hexagonal project set below. Project names follow `[Company].[Product][.{Module}]` — see [naming-convention.md](naming-convention.md). Logical structure inside `.slnx`: three top-level solution folders (`Core/`, `Host/`, `Infrastructure/`).

## Core (5 projects)

| Project | Role | Depends on |
|---|---|---|
| `Company.Product` | Application — service interfaces (`IProductService`), service implementations, business logic | `.Interface`, `.Models`, `.Constants`, `.Infrastructure` |
| `Company.Product.Interface` | Ports (data only) — `Commands/`, `Results/`, `Events/`, `Completions/`. **No `I*` types.** Shared bases `Command` / `Result` / `Event`. | `.Models`, `.Constants` |
| `Company.Product.Models` | Domain entities, value objects, aggregates | `.Constants` |
| `Company.Product.Constants` | App-wide enums (`ErrorCode`, `ProductStatus`), business constants | (nothing) |
| `Company.Product.Infrastructure` | Infrastructure abstractions — `IRepository`, `ICache`, `IStorage`, `IMessageBus`. Inner ring; no concrete tech. | `.Models`, `.Constants` |

The deliberate name overload: `Company.Product.Infrastructure` is a project under `Core/` (abstractions); `Infrastructure/` is the top-level solution folder that holds concrete adapters.

## Infrastructure adapters (N projects, demand-driven)

Concrete adapters live under the `Infrastructure/` solution folder, named directly by the technology — **no** `.Data.` / `.Persistence.` / `.Cache.` prefix.

| Project | Role |
|---|---|
| `Company.Product.SqlServer` | EF Core `DbContext`, persistence entities, repository implementations, `IXxxMapper` services. Holds migrations. |
| `Company.Product.AzureStorage` | `IStorage` implementation backed by `Azure.Storage.Blobs`. |
| `Company.Product.Redis` | `ICache` implementation backed by `StackExchange.Redis`. |
| `Company.Product.Cosmos` | Cosmos DB adapter when the store is Cosmos. |
| `Company.Product.{TechName}` | Same shape for any other backing technology. |

Adapters reference `Company.Product.Infrastructure` (for the abstractions) plus `.Models` and `.Constants`. **They never reference `.Interface` and never reference the application project.**

A stub of an external vendor (when no native emulator exists) is a technology-named host (e.g. `Company.Product.Stubs.Stripe` registered as a real Aspire resource) — not a separate hexagonal layer.

## Host (`Host/` solution folder)

| Project | Role |
|---|---|
| `Company.Product.AppHost` | Aspire orchestrator. Always present. Shared by production and tests; mode is a configuration switch. See `dotnet-aspire`. |
| `Company.Product.ServiceDefaults` | OpenTelemetry, health, resilience, service discovery (host-side class library). Required by `dotnet-aspire`. |
| `Company.Product.WebAPI` | HTTP host — controllers, ASP.NET Core pipeline. |
| `Company.Product.Worker` | Background worker host — `BackgroundService`. |
| `Company.Product.gRPC.Server` | gRPC service implementations. The `.proto` files live inside this project. |
| `Company.Product.Cli` | Command-line host. See `dotnet-system-commandline`. |
| `Company.Product.Web` | Blazor host. |
| `Company.Product.Mobile` | MAUI host. |
| `Company.Product.Desktop` | WPF / Avalonia / WinUI host. |

Hosts reference the application project, the `.Interface`, `.Models`, `.Constants`, and the concrete adapters they wire. They are the only layer that meets concrete tech and application together.

## Test (singular `Test`)

| Project | Role |
|---|---|
| `Company.Product.Test` | Single MSTest + Aspire integration-test project. Lives under `test/`, not `src/`. Categorized by surface folder (`HTTP/`, `UI/`, `gRPC/`, `Service/`, `Worker/`, `Queue/`, `Webhook/`). Per-class `DistributedApplication` mount. See [single-test-project-rule.md](single-test-project-rule.md). |

Never `.Tests`, `.UnitTests`, `.IntegrationTests`, `.E2ETests`, `.WebTests`, `.Smoke`, `.Acceptance`.

## Physical vs logical layout

- **Physical:** projects sit nested under `src/Company.Product/{Company.Product.<X>}/`; tests under `test/Company.Product/Company.Product.Test/`. See [slnx-logical-groups.md](slnx-logical-groups.md).
- **Logical:** `.slnx` solution folders (`Core/`, `Host/`, `Infrastructure/`) group projects for navigability — they never reflect physical paths.

## Dependency flow

Direction summary: Host sees everything; Application sees abstractions only; Adapters never see `.Interface` or Application. See the full reference matrix in [dependency-flow.md](dependency-flow.md).

## Enforcement

- **Architecture review:** new projects must fit the table. Inventing layer names is a finding.
- **Code review:** dependency direction is enforced (see [dependency-flow.md](dependency-flow.md)).
- **Clean-as-you-touch:** rename misnamed projects only as a coordinated refactor — surface as a TODO.
