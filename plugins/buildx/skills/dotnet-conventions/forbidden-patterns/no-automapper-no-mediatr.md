# Forbidden — third-party mappers and mediators

## Rule

**No third-party libraries for cross-cutting concerns.** Mapping and mediator-style dispatch are first-party only on this team. The replacements below are hand-written; the team does not adopt a NuGet package for either concern.

## Banned packages

```xml
<!-- Banned references -->
<!-- Mappers -->
<PackageReference Include="AutoMapper" Version="..." />
<PackageReference Include="AutoMapper.Extensions.Microsoft.DependencyInjection" Version="..." />
<PackageReference Include="Mapster" Version="..." />
<PackageReference Include="Mapster.Tool" Version="..." />
<PackageReference Include="Mapster.DependencyInjection" Version="..." />
<PackageReference Include="Riok.Mapperly" Version="..." />

<!-- Mediators -->
<PackageReference Include="MediatR" Version="..." />
<PackageReference Include="MediatR.Extensions.Microsoft.DependencyInjection" Version="..." />
<PackageReference Include="Mediator" Version="..." />
<PackageReference Include="Mediator.SourceGenerator" Version="..." />
<PackageReference Include="Brighter" Version="..." />
<PackageReference Include="Paramore.Brighter" Version="..." />
```

Pattern matches (any version, any related sub-package): `AutoMapper`, `AutoMapper.*`, `Mapster`, `Mapster.*`, `Riok.Mapperly` (any Mapperly variant), `MediatR`, `MediatR.*`, `Mediator` (martinothamar), `Mediator.*`, `Brighter`, `Paramore.Brighter`.

## Why they're banned

1. **Cross-cutting concerns must be first-party.** Mapping and dispatch behavior is small enough to write by hand and central enough to a codebase that owning the implementation outweighs any convenience savings.
2. **Reflection / assembly scanning hide bugs.** Convention-based mappers and reflection-based mediators silently miss renamed properties or unregistered handlers; first-party code surfaces these as compiler errors.
3. **Domination of taxonomy.** A third-party mediator competes with the team's `Command` / `Result` / `Event` bases and its `ErrorCode` enum; a third-party mapper competes with the hand-written `IXxxMapper` discipline.
4. **License churn.** MediatR went commercial in 2025; the team will not adopt a paid library for cross-cutting plumbing. Even free libraries shift maintenance to upstream — first-party stays under the team's control.
5. **Source generators sound free but aren't.** Mapperly and `Mediator` (martinothamar) are still external dependencies whose breaking changes the team would have to track. The hexagonal `IXxxMapper` services are written once per aggregate and never updated by a NuGet bump.

## What to do instead

### Mapping

Hand-written `IXxxMapper` services per aggregate, with explicit `ToEntity` / `ToDomain` methods, injected into the repository that needs them. Canonical shape lives in `dotnet-hexagonal-architecture` § core-and-infrastructure § Mappers — first-party only.

```csharp
public interface IProductMapper
{
    ProductEntity ToEntity(Product domain);
    Product ToDomain(ProductEntity entity);
}

public class ProductMapper : IProductMapper
{
    public ProductEntity ToEntity(Product domain) => new()
    {
        ProductId = domain.ProductId,
        Name = domain.Name,
        // ...
    };

    public Product ToDomain(ProductEntity entity) => new()
    {
        ProductId = entity.ProductId,
        Name = entity.Name,
        // ...
    };
}
```

The mapper lives in the adapter project that owns the persistence entity (`Acme.Inventory.SqlServer/Mappers/`), is registered in the host's DI, and is consumed by the repository.

### "Mediator-like" needs

**Default = no mediator.** Direct service calls and delegate-first events (raised only from application services) cover virtually every dispatch need. Events follow the hexagonal `Event` base with hand-rolled C# delegates — see `dotnet-hexagonal-architecture` § core-and-infrastructure § Events.

If a concrete need surfaces (cross-process delivery, persistence, broad fan-out across decoupled modules), **escalate to architecture review** for a first-party `IXxxDispatcher`-style abstraction. Do not pull a NuGet package unilaterally.

## Enforcement

- **Banned packages list:** `AutoMapper`, `AutoMapper.*`, `Mapster`, `Mapster.*`, `Riok.Mapperly`, `MediatR`, `MediatR.*`, `Mediator` (martinothamar), `Mediator.*`, `Brighter`, `Paramore.Brighter`.
- **On sight, inside a file you're editing:** swap the call site to the hand-written `IXxxMapper` (or direct service call). If the first-party replacement does not yet exist, surface as a TODO — do not introduce a competing first-party abstraction unilaterally.
- **Quick scan:**

  ```bash
  grep -rE "PackageReference Include=\"(AutoMapper|Mapster|Riok\.Mapperly|MediatR|Mediator|Brighter|Paramore\.Brighter)" src/ \
    && echo "BANNED PACKAGE FOUND"
  ```

  must return no matches.

## See also

- `dotnet-hexagonal-architecture` § core-and-infrastructure § Mappers and § Events.
- `dotnet-hexagonal-architecture` rule 8 — "No third-party libraries for cross-cutting concerns".
- [../source-generators/index.md](../source-generators/index.md) — only BCL / first-party Microsoft generators are accepted.
