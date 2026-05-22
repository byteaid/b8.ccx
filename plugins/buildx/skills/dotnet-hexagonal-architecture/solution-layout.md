# Solution Layout

The split between **logical** (`.slnx` solution folders) and **physical** (on-disk directories) is deliberate — the solution view organizes projects by role, while the disk stays flat to keep paths short and references trivial.

## Logical layout (`.slnx` solution folders)

Three top-level solution folders. Inside `Core/`, the application project sits as a sibling to four supporting projects.

```
Company.Product.slnx
├── Core/
│   ├── Company.Product
│   ├── Company.Product.Interface
│   ├── Company.Product.Models
│   ├── Company.Product.Constants
│   └── Company.Product.Infrastructure
├── Host/
│   ├── Company.Product.AppHost                # .NET Aspire — canonical orchestration
│   ├── Company.Product.WebAPI
│   ├── Company.Product.Worker
│   └── Company.Product.gRPC.Server
└── Infrastructure/
    ├── Company.Product.SqlServer
    ├── Company.Product.AzureStorage
    └── Company.Product.Redis
```

The deliberate name overload around "infrastructure":

| | What it is | Where it lives |
|---|---|---|
| **`Company.Product.Infrastructure`** project | Declares the abstractions (`IRepository`, `ICache`, `IStorage`, `IMessageBus`). Inner ring; no concrete tech. | Inside the `Core/` solution folder. |
| **`Infrastructure/`** solution folder | Holds the concrete implementations, named after the technology (`.SqlServer`, `.AzureStorage`, `.Redis`, …). | Top-level solution folder, sibling of `Core/` and `Host/`. |

## Physical layout (on disk)

Solution folders are pure metadata; they do not exist as directories. Every project is a flat sibling under `src/Company.Product/`. Tests sit at the same level as `src/` and `docs/`, never under `src/`.

```
/
├── Company.Product.slnx
├── docs/
│   └── ...
├── src/
│   └── Company.Product/
│       ├── Company.Product/
│       │   └── Company.Product.csproj                         # Core (drops the ".{Module}" suffix)
│       ├── Company.Product.Interface/
│       │   └── Company.Product.Interface.csproj
│       ├── Company.Product.Models/
│       │   └── Company.Product.Models.csproj
│       ├── Company.Product.Constants/
│       │   └── Company.Product.Constants.csproj
│       ├── Company.Product.Infrastructure/
│       │   └── Company.Product.Infrastructure.csproj          # IRepository, ICache, IStorage, …
│       ├── Company.Product.SqlServer/
│       │   └── Company.Product.SqlServer.csproj
│       ├── Company.Product.AzureStorage/
│       │   └── Company.Product.AzureStorage.csproj
│       ├── Company.Product.Redis/
│       │   └── Company.Product.Redis.csproj
│       ├── Company.Product.AppHost/
│       │   └── Company.Product.AppHost.csproj                 # .NET Aspire AppHost
│       ├── Company.Product.WebAPI/
│       │   └── Company.Product.WebAPI.csproj
│       ├── Company.Product.Worker/
│       │   └── Company.Product.Worker.csproj
│       └── Company.Product.gRPC.Server/
│           └── Company.Product.gRPC.Server.csproj
└── test/
    └── Company.Product/
        └── Company.Product.Test/
            └── Company.Product.Test.csproj                    # MSTest + Aspire.Hosting.Testing
```

## Naming rules

| Slot | Rule | Example |
|---|---|---|
| Project root | `[Company].[Product].[Module]` | `Acme.Inventory.WebAPI` |
| Application (Core) project | drop the `.{Module}` suffix — just `[Company].[Product]` | `Acme.Inventory` |
| Concrete infrastructure adapter | use the technology name directly; **no** `.Data.` / `.Persistence.` / `.Cache.` prefixes | `Acme.Inventory.SqlServer`, `Acme.Inventory.AzureStorage`, `Acme.Inventory.Redis` |
| Host | name by the surface | `.WebAPI`, `.Worker`, `.gRPC.Server`, `.Cli`, `.Web` (Blazor), `.Mobile` (MAUI), `.Desktop` (WPF / Avalonia / WinUI) |
| AppHost (Aspire) | always `[Company].[Product].AppHost` | `Acme.Inventory.AppHost` |
| Test project | always `[Company].[Product].Test` (singular `Test`) | `Acme.Inventory.Test` |

## Greenfield checklist

When the user asks for a brand-new solution under this architecture:

1. Pick the `[Company].[Product]` root. Confirm with the user.
2. Create the `.slnx` and the three solution folders (`Core/`, `Host/`, `Infrastructure/`).
3. Add the five Core projects: `[Company].[Product]`, `.Interface`, `.Models`, `.Constants`, `.Infrastructure`.
4. Add the AppHost: `[Company].[Product].AppHost` under `Host/`. (The `dotnet-aspire` skill covers `aspire-apphost` template usage.)
5. Add at least one host (typically `.WebAPI`) under `Host/`.
6. Add the test project `[Company].[Product].Test` under `test/` (template `aspire-mstest`).
7. Defer adding adapters under `Infrastructure/` until the first abstraction is used by the application — adapters appear demand-driven, not pre-emptively.

Project references (set up in this exact direction):

- `[Company].[Product]` → `.Interface`, `.Models`, `.Constants`, `.Infrastructure`.
- `[Company].[Product].Infrastructure` → `.Models`, `.Constants`.
- `[Company].[Product].Interface` → `.Models`, `.Constants`.
- `[Company].[Product].Models` → (none, leaf).
- `[Company].[Product].Constants` → (none, leaf).
- Adapters (e.g. `.SqlServer`) → `[Company].[Product].Infrastructure`, `.Models`, `.Constants`. **Never** `.Interface`. **Never** the application project.
- Hosts (`.WebAPI`, etc.) → `[Company].[Product]`, `.Interface`, `.Models`, `.Constants`, plus the adapters they wire.
- `Company.Product.AppHost` → references the host projects through `Projects.Company_Product_*` generated types (Aspire convention).
- `Company.Product.Test` → `Company.Product.AppHost` (and only the AppHost).

## Existing-repo rule

If the repo already exists with a different layout, **do not migrate**. Match the project's actual structure when adding code. A migration to hexagonal happens only when the user explicitly asks for it — and even then, plan the move file by file before touching anything (use `EnterPlanMode`).

## Cross-references

- [interface-and-bases.md](interface-and-bases.md) — what goes in `.Interface` and `.Constants`.
- [core-and-infrastructure.md](core-and-infrastructure.md) — what goes in `Company.Product`, `.Infrastructure`, and the adapters.
- [dependency-flow.md](dependency-flow.md) — the reference matrix in tabular form.
- The `dotnet-aspire` skill — Aspire templates, AppHost csproj shape.
