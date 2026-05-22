# `.slnx` logical groupings

> Authoritative source: `dotnet-hexagonal-architecture` § solution-layout.

## Rule

The team uses `.slnx` (XML solution files, current SDK format) — not the legacy `.sln`. Solution folders inside `.slnx` are **logical groupings only**; they do not exist on disk. The three top-level solution folders are **`Core/`, `Host/`, `Infrastructure/`** — fixed by the hexagonal architecture.

## Rationale

- `.slnx` is the SDK's modern solution format — smaller, mergeable, no GUID noise.
- The three top-level folders mirror the hexagonal split: Core (pure logic + ports), Host (composition roots), Infrastructure (concrete adapters).
- Decoupling logical from physical lets the IDE view stay tidy without dictating file paths.

## Physical layout (on disk)

Projects live nested under `src/Company.Product/{Company.Product.<X>}/`. Tests live under `test/Company.Product/Company.Product.Test/`, never under `src/`.

```
/
├── Acme.Inventory.slnx
├── docs/
├── src/
│   └── Acme.Inventory/
│       ├── Acme.Inventory/                      (Core / application)
│       ├── Acme.Inventory.Interface/
│       ├── Acme.Inventory.Models/
│       ├── Acme.Inventory.Constants/
│       ├── Acme.Inventory.Infrastructure/       (abstractions only)
│       ├── Acme.Inventory.SqlServer/            (concrete adapter — no .Data. prefix)
│       ├── Acme.Inventory.Redis/
│       ├── Acme.Inventory.AzureStorage/
│       ├── Acme.Inventory.AppHost/
│       ├── Acme.Inventory.ServiceDefaults/
│       ├── Acme.Inventory.WebAPI/
│       └── Acme.Inventory.Worker/
└── test/
    └── Acme.Inventory/
        └── Acme.Inventory.Test/                 (singular `.Test`)
```

## Logical layout (`.slnx`)

```
Acme.Inventory.slnx
├── Core
│   ├── Acme.Inventory
│   ├── Acme.Inventory.Interface
│   ├── Acme.Inventory.Models
│   ├── Acme.Inventory.Constants
│   └── Acme.Inventory.Infrastructure
├── Host
│   ├── Acme.Inventory.AppHost
│   ├── Acme.Inventory.ServiceDefaults
│   ├── Acme.Inventory.WebAPI
│   └── Acme.Inventory.Worker
└── Infrastructure
    ├── Acme.Inventory.SqlServer
    ├── Acme.Inventory.Redis
    └── Acme.Inventory.AzureStorage
```

The single test project (`Acme.Inventory.Test`) is added to the `.slnx` but lives under `test/` on disk; it does not need its own top-level solution folder.

## CLI verbs

```bash
# Create / migrate
dotnet new sln -n Acme.Inventory -f slnx
dotnet sln Acme.Inventory.sln migrate            # legacy .sln → .slnx

# Add projects
dotnet sln Acme.Inventory.slnx add src/Acme.Inventory/Acme.Inventory.WebAPI/Acme.Inventory.WebAPI.csproj
dotnet sln Acme.Inventory.slnx add test/Acme.Inventory/Acme.Inventory.Test/Acme.Inventory.Test.csproj

# Solution folders are edited in the .slnx XML (the CLI does not yet have a verb for them).
```

## Editing `.slnx` solution folders

`.slnx` is XML; folders are `<Folder>` elements with the canonical names `/Core/`, `/Host/`, `/Infrastructure/`.

```xml
<Solution>
  <Folder Name="/Core/">
    <Project Path="src/Acme.Inventory/Acme.Inventory/Acme.Inventory.csproj" />
    <Project Path="src/Acme.Inventory/Acme.Inventory.Interface/Acme.Inventory.Interface.csproj" />
    <Project Path="src/Acme.Inventory/Acme.Inventory.Models/Acme.Inventory.Models.csproj" />
    <Project Path="src/Acme.Inventory/Acme.Inventory.Constants/Acme.Inventory.Constants.csproj" />
    <Project Path="src/Acme.Inventory/Acme.Inventory.Infrastructure/Acme.Inventory.Infrastructure.csproj" />
  </Folder>
  <Folder Name="/Host/">
    <Project Path="src/Acme.Inventory/Acme.Inventory.AppHost/Acme.Inventory.AppHost.csproj" />
    <Project Path="src/Acme.Inventory/Acme.Inventory.WebAPI/Acme.Inventory.WebAPI.csproj" />
  </Folder>
  <Folder Name="/Infrastructure/">
    <Project Path="src/Acme.Inventory/Acme.Inventory.SqlServer/Acme.Inventory.SqlServer.csproj" />
    <Project Path="src/Acme.Inventory/Acme.Inventory.Redis/Acme.Inventory.Redis.csproj" />
  </Folder>
  <Project Path="test/Acme.Inventory/Acme.Inventory.Test/Acme.Inventory.Test.csproj" />
</Solution>
```

This is a deliberate hand-edit — the CLI does not yet manage folders. Keep the XML diff reviewable.

## Enforcement

- **No `.sln`** in new repos; migrate any existing one to `.slnx` on the first touch.
- **Three logical folders only:** `Core/`, `Host/`, `Infrastructure/`. Other folders (e.g. `Domain`, `Adapters`, `Tests`) are not used.
- **Code review:** flag any `<Project Path="..."/>` whose disk path does not nest under `src/Company.Product/` or `test/Company.Product/`.
