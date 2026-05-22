# Integration Testing — Moved

The MSTest integration testing recipe (single `Company.Product.Test` project, per-class `DistributedApplicationTestingBuilder` mount, parallelism settings, `Blaztrap.Aspire.FileLogging` wiring inside `[ClassInitialize]`, consolidated `TestResults/{run-id}/...` artefact layout) now lives in the **`dotnet-testing`** skill.

Load `dotnet-testing` and read:

- `dotnet-testing` § layout — single project, surface folders, naming.
- `dotnet-testing` § mstest-integration — per-class mount, parallelism, file logging, artefact layout.
- `dotnet-testing` § seeding — the four canonical seeding strategies.
- `dotnet-testing` § forbidden-patterns — bans on third-party mocks, test branches in production code, seed endpoints, mocks in consumer DI.

This skill (`dotnet-aspire`) still owns the producer-side primitives: `DistributedApplicationTestingBuilder.CreateAsync`, `AddFileLogging` (see [file-logging.md](file-logging.md)), `RunAsEmulator()` / `AsExisting` switching (see [emulators-and-real-infra.md](emulators-and-real-infra.md)), `AddProject` / `AddContainer` / `AddExecutable` registration verbs (see [apphost-wiring.md](apphost-wiring.md)).
