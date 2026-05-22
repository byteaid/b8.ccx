# Dependency flow

> Authoritative source: `dotnet-hexagonal-architecture` § dependency-flow. This leaf is a tight summary; the hexagonal skill wins on any disagreement.

## Rule

Project references are governed by the reference matrix below. Adapters never see `.Interface`. Application never references concrete adapters. Composition lives in Host alone.

## Reference matrix

Rows reference columns. ✓ allowed, ✗ forbidden, — N/A.

| ↓ from / → to              | Interface | Models | Constants | Infrastructure (abstractions) | Adapters | Application |
|----------------------------|:---------:|:------:|:---------:|:-----------------------------:|:--------:|:-----------:|
| Host                       | ✓         | ✓      | ✓         | ✓                             | ✓        | ✓           |
| Application (`Company.Product`) | ✓    | ✓      | ✓         | ✓                             | ✗        | —           |
| Adapters (`.SqlServer`, `.Redis`, …) | ✗ | ✓      | ✓         | ✓                             | —        | ✗           |
| Infrastructure abstractions (`.Infrastructure`) | ✗ | ✓ | ✓ | —                          | ✗        | ✗           |
| Interface (`.Interface`)   | —         | ✓      | ✓         | ✗                             | ✗        | ✗           |
| Models                     | ✗         | —      | ✓         | ✗                             | ✗        | ✗           |
| Constants                  | ✗         | ✗      | —         | ✗                             | ✗        | ✗           |

## Invariants

- **Infrastructure → Interface: forbidden.** Neither the abstractions project nor the adapters reference `Company.Product.Interface`. Commands/Results/Events live on the Host ↔ Application boundary only.
- **Application → Adapters: forbidden.** The application never references `.SqlServer` / `.Redis` / `.AzureStorage`. It only knows the abstractions in `Company.Product.Infrastructure`.
- **Adapters → Application: forbidden.** Adapters do not reach back into application services.
- **Adapters → Adapters: forbidden.** Adapters do not chain.
- **Composition lives in Host alone.** Swapping `.Redis` for `.Memcached` is a Host-only change.
- **Interface → Infrastructure: forbidden.** Interface is data; it does not know about repositories, caches, or storage.
- **Models → anything stateful: forbidden.** Models contains data shapes only.
- **Circular references of any kind: forbidden.**

## Canonical sample — the Core project

```xml
<!-- src/Acme.Inventory/Acme.Inventory/Acme.Inventory.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Acme.Inventory.Interface\Acme.Inventory.Interface.csproj" />
    <ProjectReference Include="..\Acme.Inventory.Models\Acme.Inventory.Models.csproj" />
    <ProjectReference Include="..\Acme.Inventory.Constants\Acme.Inventory.Constants.csproj" />
    <ProjectReference Include="..\Acme.Inventory.Infrastructure\Acme.Inventory.Infrastructure.csproj" />
  </ItemGroup>
  <!-- NO PackageReference to EF Core, ASP.NET Core, Aspire, anything external -->
</Project>
```

If `dotnet restore` on the Core project pulls in `Microsoft.EntityFrameworkCore.*` or any concrete adapter, something violated the rule.

## PR review checklist

1. Open every touched `.csproj`. Check `<ProjectReference>` lines against the matrix.
2. Check `using` directives. A `using Company.Product.SqlServer` inside `Company.Product` is a violation. A `using Company.Product.Interface` inside `Company.Product.SqlServer` is a violation.
3. Check namespace placement. `IProductRepository` under `Company.Product.Interface.*` is a violation (it belongs in `Company.Product.Infrastructure.*`).
4. Check `ErrorCode` usage. New failure modes go in the existing app-wide enum, not in a sibling enum.

## Enforcement

- **Code review:** every new `<ProjectReference>` is checked against the matrix.
- **Architectural test (recommended):** add a tiny test in `Company.Product.Test` that asserts Core's `.csproj` has no infrastructure package references.
- **Clean-as-you-touch:** removing a forbidden reference cascades — surface as a TODO; do not silently rip out a transitive dependency that compiled by accident.
