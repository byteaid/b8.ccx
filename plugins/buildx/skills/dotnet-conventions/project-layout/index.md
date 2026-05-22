# .NET conventions — Project layout

Hexagonal naming, dependency flow, logical groupings in `.slnx`. Projects live nested under `src/Company.Product/`; tests under `test/Company.Product/`. Solution folders are logical metadata only.

## Final topics

| Trigger | File |
|---|---|
| Full table of projects, purpose, and dependencies (canonical hexagonal layout) | [hexagonal-layers.md](hexagonal-layers.md) |
| Reference matrix per hexagonal — adapters never see `.Interface`; Application never references concrete adapters; Host is the only composition root | [dependency-flow.md](dependency-flow.md) |
| `.slnx` logical groupings (`Core/`, `Host/`, `Infrastructure/`) vs nested physical layout | [slnx-logical-groups.md](slnx-logical-groups.md) |
| Naming — `[Company].[Product][.{Module}]`, Core drops the suffix, adapters use the technology name directly | [naming-convention.md](naming-convention.md) |
| Database adapter projects — technology-named (`.SqlServer`, `.Cosmos`, `.Redis`) with no `.Data.` prefix | [data-access-projects.md](data-access-projects.md) |
| The single test project rule — `[Company].[Product].Test` (singular), per-class AppHost mount (pointer; full rule in `dotnet-testing`) | [single-test-project-rule.md](single-test-project-rule.md) |
| Canonical hexagonal authority — load when a topic disagrees with the leaves below | `dotnet-hexagonal-architecture` |

## See also

- [../forbidden-patterns/no-proto-outside-dedicated-project.md](../forbidden-patterns/no-proto-outside-dedicated-project.md)
