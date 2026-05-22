---
name: dotnet-hexagonal-architecture
description: Team-canonical hexagonal (ports-and-adapters) architecture for .NET 10 / C# 14 — `.slnx` solution folders (`Core/Host/Infrastructure`), flat `src/Company.Product/` on disk, the `Company.Product` / `.Interface` / `.Models` / `.Constants` / `.Infrastructure` / technology-named adapters / `.AppHost` / host project breakdown, shared `Command` / `Result` / `Event` bases, app-wide `ErrorCode` enum, hand-written `IXxxMapper` services, delegate-first events raised only by application services, single `Company.Product.Test` project, and the dependency-flow invariants (Infrastructure never sees Interface; Application never references concrete adapters). Test layout, MSTest mechanics, and seeding are owned by `dotnet-testing`.
when_to_use: |
  - Triggers: hexagonal, ports and adapters, `.slnx`, Company.Product, .Interface / .Models / .Constants / .Infrastructure, ErrorCode, Command/Result/Event base, IRepository/ICache/IStorage, IXxxMapper, Company.Product.Test, blank/greenfield .NET solution.
  - Tasks: lay out a new solution; place a new type; add a Command/Result/Event/`ErrorCode`; add an adapter or host; place a test class under `Company.Product.Test/{Category}/`; audit a PR against the dependency-flow invariants.
  - Hexagonal is the default ONLY for greenfield/blank projects. In existing repos the project's current architecture wins — do not migrate or reorganize unless the user explicitly asks.
allowed-tools: Bash, Edit, Glob, Grep, Monitor, NotebookEdit, PowerShell, Read, Write
user-invocable: false
paths: ["**/*.slnx", "**/Company.Product*.csproj", "**/Company.Product*/**/*.cs", "**/Company.Product.Test/**"]
---

# .NET Hexagonal Architecture — Authoring Reference

L1 dispatcher. Concrete chapters live in L2 sub-files. This file carries the rules every invocation must respect (positioned to survive compaction) and the dispatch table.

## Mental model

The team's hexagonal layout has three solution folders — `Core`, `Host`, `Infrastructure` — and one fixed project triplet inside `Core/`: the application project (`Company.Product`), four pure-data sibling projects (`.Interface`, `.Models`, `.Constants`), plus `.Infrastructure` declaring abstractions. Concrete adapters live in the top-level `Infrastructure/` folder under technology names (`.SqlServer`, `.AzureStorage`, `.Redis`). Hosts (`WebAPI`, `Worker`, `gRPC.Server`, `Cli`, `Web`, …) are the only place that sees both the application and a concrete adapter; everything else only meets at abstractions. Aspire (`Company.Product.AppHost`) is the canonical orchestrator — same project for production and tests.

## Non-negotiable rules (must survive compaction)

1. **Architecture domination — never refactor without being asked.** In an existing project the project's current architecture is authoritative; do exactly the task that was requested and stop. If a user asks "enroll this app in Aspire", add Aspire and stop — do not also reorganize folders into `Core/Host/Infrastructure`. Migrations to hexagonal happen only when the user explicitly asks for them. **Hexagonal is the default only when starting a greenfield / blank project.**
2. **Project naming = `[Company].[Product].[Module]`.** Core project drops `.{Module}` (it is just `Company.Product`). Concrete adapters use the technology name directly (`Company.Product.SqlServer`, never `.Data.SqlServer`). Hosts are named by their surface (`Company.Product.WebAPI`, `.Worker`, `.gRPC.Server`, `.Cli`, `.Web`, `.Mobile`, `.Desktop`).
3. **Solution folders ≠ physical folders.** `Core/`, `Host/`, `Infrastructure/` exist only in the `.slnx`. On disk every project sits flat under `src/Company.Product/` (and tests under `test/Company.Product/Company.Product.Test/`). See [solution-layout.md](solution-layout.md).
4. **`.Interface` is data only.** Four sub-folders: `Commands/`, `Results/`, `Events/`, `Completions/`. **No `I*` interface types live there** — service interfaces (e.g. `IProductService`) belong in the application project (`Company.Product`); infrastructure abstractions (`IRepository`, `ICache`, `IStorage`) belong in `Company.Product.Infrastructure`. See [interface-and-bases.md](interface-and-bases.md).
5. **Shared bases are bare names: `Command`, `Result`, `Event`.** Every concrete `*Command`/`*Result`/`*Event` derives from the matching base (carrying `CommandId`, timestamps). Never `BaseCommand`/`BaseResult`/`BaseEvent`. See [interface-and-bases.md](interface-and-bases.md).
6. **`ErrorCode` is a single app-wide enum** in `Company.Product.Constants`. Numeric ranges (1xxx validation, 2xxx auth, 3xxx business, 4xxx resource state, 5xxx infra, 6xxx quotas, 7xxx external, 9xxx catch-all). Never split into per-module enums; never replace with free-form `string Message`. The frontend translates the code into a user-facing message. See [interface-and-bases.md](interface-and-bases.md) § ErrorCode.
7. **Events end in `Event`, not `EventArgs`.** Derive from the shared `Event` base. **Do not inherit from `System.EventArgs`.** Declare delegate-first (`public delegate void XxxHandler(object? sender, XxxEvent e);`). Raised **only** from application services. No bus / mediator / third-party until a concrete need appears (cross-process, persistence, broad fan-out). See [core-and-infrastructure.md](core-and-infrastructure.md) § Events.
8. **No third-party libraries for cross-cutting concerns.** Mapping uses hand-written `IXxxMapper` services (one per aggregate, with explicit `ToEntity`/`ToDomain`) — never AutoMapper, Mapster, or convention-based mappers. Generalize to mediators, validation, DI: prefer BCL + a small first-party abstraction first. See [core-and-infrastructure.md](core-and-infrastructure.md) § Mappers.
9. **Infrastructure never references `.Interface`.** Neither the abstractions project nor the adapters know about Commands/Results/Events. The `.Interface` project is the **Host ↔ Application** contract surface only. See [dependency-flow.md](dependency-flow.md).
10. **Application never references concrete adapters.** `Company.Product` only knows the abstractions in `Company.Product.Infrastructure`. The Host wires concrete adapters at the composition root. Swapping `.Redis` for `.Memcached` is a Host-only change.
11. **Tests live in the `dotnet-testing` skill.** This architecture mandates a single `Company.Product.Test` project (singular `Test`) categorized by surface folder, with per-class `DistributedApplication` mount, integration tests only — but the layout, MSTest mechanics, seeding strategies, and forbidden patterns are owned by the `dotnet-testing` skill. Load that skill the moment a test file or test mechanic is in scope.
12. **Single `Company.Product.AppHost`** under `src/Host/`. Production and tests reference the same project; mode (real infra vs emulators/stubs) is a configuration switch read from `builder.Configuration`, not a separate AppHost. See the `dotnet-aspire` skill.

## Project breakdown

| Solution folder | Project | Role | What goes here |
|---|---|---|---|
| `Core/` | `Company.Product` | Application | Service interfaces (`IProductService`), service implementations, business logic. |
| `Core/` | `Company.Product.Interface` | Ports (data) | `Commands/`, `Results/`, `Events/`, `Completions/`. Shared `Command`/`Result`/`Event` bases. **No `I*` types.** |
| `Core/` | `Company.Product.Models` | Domain | Entities, value objects, aggregates. |
| `Core/` | `Company.Product.Constants` | Constants | `ErrorCode` (app-wide), `ProductStatus`, business constants. |
| `Core/` | `Company.Product.Infrastructure` | Ports (infra abstractions) | `IRepository`, `ICache`, `IStorage`, `IMessageBus`. **Inner ring — no concrete tech.** |
| `Infrastructure/` | `Company.Product.SqlServer` | Outbound adapter | EF Core `DbContext`, persistence entities, `*Repository` implementations, `IXxxMapper` services. |
| `Infrastructure/` | `Company.Product.AzureStorage` | Outbound adapter | `IStorage` implementation backed by `Azure.Storage.Blobs`. |
| `Infrastructure/` | `Company.Product.Redis` | Outbound adapter | `ICache` implementation backed by `StackExchange.Redis`. |
| `Host/` | `Company.Product.AppHost` | Aspire orchestration | `Program.cs` declaring resources; shared by prod and tests. |
| `Host/` | `Company.Product.WebAPI` / `.Worker` / `.gRPC.Server` / `.Cli` / `.Web` / `.Mobile` / `.Desktop` | Inbound adapters | Process entry points + DI composition + transport-to-Command translation. |
| (test) | `Company.Product.Test` | Single test project | Closed-box integration tests. Mechanics and layout owned by the `dotnet-testing` skill. |

## Sub-index

| Topic | File | Load when |
|---|---|---|
| Solution layout: `.slnx` solution folders, flat physical layout, naming rules per project type | [solution-layout.md](solution-layout.md) | Creating a new solution, adding a new project, deciding where a `.csproj` goes on disk. |
| `.Interface` project: Commands/Results/Events/Completions, shared `Command`/`Result`/`Event` bases, `ErrorCode` taxonomy with numeric ranges | [interface-and-bases.md](interface-and-bases.md) | Adding a new Command/Result/Event, extending `ErrorCode`, deciding what belongs in `.Interface` vs `.Models` vs `.Constants`. |
| Application services + infrastructure abstractions + concrete adapters + `IXxxMapper` services + delegate-first events raised from application services | [core-and-infrastructure.md](core-and-infrastructure.md) | Adding a service, adding an adapter, adding a mapper, raising an event, wiring DI in a host. |
| Dependency-flow diagram, reference matrix, invariants, end-to-end implementation example | [dependency-flow.md](dependency-flow.md) | Reviewing whether a PR respects the layering rules; checking which project may reference which. |

## Quick decision matrix

| Need | Pick |
|---|---|
| Brand-new (greenfield) .NET solution | Apply hexagonal as the default. Start with [solution-layout.md](solution-layout.md). |
| Existing repo, user asks for a focused change (e.g. add a feature, enroll in Aspire) | Stay in scope. Do not propose a hexagonal migration. |
| Existing repo, user explicitly asks "migrate to hexagonal" | Use this skill end-to-end; build a step-by-step plan before moving any file. |
| Add a Command / Result / Event | [interface-and-bases.md](interface-and-bases.md). |
| Add an `ErrorCode` value | [interface-and-bases.md](interface-and-bases.md) § ErrorCode — pick the right numeric range; never reuse a code; never delete one. |
| Add a new infrastructure adapter | [core-and-infrastructure.md](core-and-infrastructure.md) § Adapters. Implement abstractions from `Company.Product.Infrastructure`; reference `.Models` + `.Constants` only. |
| Add a new host (WebAPI/Worker/gRPC/Cli/Blazor/MAUI/WPF) | [core-and-infrastructure.md](core-and-infrastructure.md) § Hosts. Translate transport input to a Command; map `Result` subtypes to the surface's response shape. |
| Add a test | The `dotnet-testing` skill owns layout, MSTest mechanics, seeding, and the related forbidden patterns. |
| Wire Aspire / pick the registration verb / switch emulator vs real | The `dotnet-aspire` skill. |

## Cross-references

- Live (.NET 10): https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/
- Live (C# 14): https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/
- Live (.slnx solution format): https://learn.microsoft.com/en-us/visualstudio/ide/solutions-projects-overview
- Related skill: `dotnet-aspire` — AppHost wiring, emulator/real-infra switching, file logging.
- Related skill: `dotnet-testing` — single `Company.Product.Test` layout, MSTest per-class mount, seeding strategies, testing-related forbidden patterns.
- Related skill: `dotnet-system-commandline` — when the host is a CLI.
- Related skill: `dotnet-file-based-apps` — when a sidecar tool is a single `.cs` file.
- Repo rules: `AGENTS.md` § Skills.
