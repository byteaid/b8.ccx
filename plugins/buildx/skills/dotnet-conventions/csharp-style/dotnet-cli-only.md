# `dotnet` CLI for all project / solution / package management

## Rule

Use the `dotnet` CLI for every solution-, project-, and package-level operation. Never hand-edit `.csproj`, `.sln`, `.slnx`, or `Directory.Packages.props` for these tasks. The team is editor-agnostic; Visual Studio-specific workflows are out of scope.

## Rationale

- Reproducible: `dotnet add ...` produces consistent output regardless of OS, shell, or editor.
- Reviewable: command transcripts trace exactly what changed; hand-edits diff into ambiguous XML.
- Tooling-aligned: `dotnet sln`, `dotnet new`, `dotnet add`, `dotnet remove` honor the SDK version pinned in `global.json`.
- CLI works identically in CI, on a contributor's machine, and from an agent — no IDE assumptions.

## Canonical commands

```bash
# Solution operations
dotnet new sln -n {Company}.{Product} -f slnx
dotnet sln {Company}.{Product}.slnx add src/{Company}.{Product}/{Company}.{Product}.csproj

# Project scaffolding
dotnet new classlib -n {Company}.{Product}.Models -o src/{Company}.{Product}.Models -f net10.0
dotnet new webapi   -n {Company}.{Product}.WebAPI -o src/{Company}.{Product}.WebAPI -f net10.0
dotnet new mstest   -n [Company].[Product].Test   -o test/[Company].[Product]/[Company].[Product].Test  -f net10.0

# Package + project references
dotnet add src/{Company}.{Product}.WebAPI package Microsoft.EntityFrameworkCore.SqlServer
dotnet add src/{Company}.{Product}.WebAPI reference src/{Company}.{Product}.Models/{Company}.{Product}.Models.csproj

# Build / restore / clean
dotnet restore
dotnet build -warnaserror
dotnet clean
```

For Aspire-specific scaffolding (AppHost, ServiceDefaults), the verbs in `dotnet-aspire` § scaffolding extend this — same CLI, same discipline.

## When hand-editing IS appropriate

- Adding `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`, or other one-time property tweaks the CLI does not generate by default.
- Editing `Directory.Build.props` / `Directory.Packages.props` for centralized package versioning (Central Package Management is a deliberate file).
- Adding `<Protobuf Include="..." />` items to a gRPC project — the CLI has no first-class verb for this.

The litmus test: if there is a `dotnet` verb that does the operation, use it.

## Enforcement

- **Code review:** flag `.csproj` diffs that add a `<PackageReference>` or `<ProjectReference>` by hand when `dotnet add` would have done it. Re-do via the CLI and verify the diff matches.
