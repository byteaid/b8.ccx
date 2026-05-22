---
name: dotnet-aspire
description: Authoring reference for .NET Aspire 13.2 on .NET 10. Scaffolding an AppHost, enrolling existing projects (Compose or unstructured), wiring per project type with the right startup operator, switching emulators vs real infra behind one binary flag, Playwright pointed at Aspire-allocated endpoints, a symptom-cause-fix troubleshooting catalogue, aspire publish / deploy with PublishAsDockerfile / PublishAsAzureContainerApp / azd / Compose / k8s publishers, and Blaztrap.Aspire.FileLogging for per-resource and AppHost log files. Testing mechanics (DistributedApplicationTestingBuilder, MSTest layout, seeding strategies) live in the `dotnet-testing` skill.
when_to_use: |
  - Trigger keywords: Aspire, AppHost, DistributedApplication, AddProject, AddExecutable, AddNpmApp, AddViteApp, RunAsEmulator, AsExisting, AddExternalService, WithReference, WaitFor, WithExplicitStart, PublishAsDockerfile, PublishAsAzureContainerApp, aspire publish, aspire deploy, azd up, Blaztrap.Aspire.FileLogging.
  - Task shapes: scaffold a solution; enroll an existing repo; pick the verb + startup operator per project; wire emulators vs real infra behind one flag; wire Playwright at Aspire endpoints; diagnose stuck / failing / flaky runs; publish or deploy via aspire publish / deploy / azd; capture per-resource and AppHost logs.
allowed-tools: Bash, Edit, Glob, Grep, Monitor, NotebookEdit, PowerShell, Read, Write
user-invocable: false
paths: ["**/*.AppHost.csproj", "**/*.ServiceDefaults.csproj", "**/aspire.json", "**/AppHost/Program.cs"]
---

# .NET Aspire — Authoring Reference

L1 dispatcher. Concrete content lives in L2 sub-files. Keep this file small enough to survive compaction with the rules and the dispatch table intact.

## Mental model

.NET Aspire is an opinionated orchestrator for distributed .NET applications. The **AppHost** project declares every component (projects, containers, executables, parameters, cloud resources) as C# code. At dev time Aspire runs the topology under the Developer Control Plane (DCP) with dashboard, automatic service discovery, OpenTelemetry, health checks, resilience, and waited startup. At publish time it emits target-specific artifacts (Docker Compose, Kubernetes, Azure Container Apps, App Service).

Two artifact families:

| Artifact | NuGet pattern | Project | Extends |
|---|---|---|---|
| Hosting integration | `Aspire.Hosting.<Vendor>.<Product>` | AppHost | `IDistributedApplicationBuilder` |
| Client integration | `Aspire.<Vendor>.<Product>` | Consumer | `IHostApplicationBuilder` |

This skill is about **authoring**: scaffolding, wiring, testing, and log capture. Architectural deep-dives (resource model internals, custom resources, publishing pipelines) are out of scope — link to upstream docs.

## Non-negotiable rules (must survive compaction)

0. **Stay in scope; don't refactor uninstructed.** When the user asks for an Aspire-specific change (enroll an app, add an integration, write a test, switch emulator/real, capture logs), do exactly that and stop. Never propose or apply a hexagonal-architecture migration as a side effect — even if the project's current layout doesn't match. Architecture decisions belong to the `dotnet-hexagonal-architecture` skill, and they activate only on explicit user request or on a greenfield/blank project.
1. **Two projects, never one.** A real Aspire solution has an `AppHost` (orchestration) AND a `ServiceDefaults` class library (telemetry / discovery / health / resilience). Every consumer project calls `builder.AddServiceDefaults()` and `app.MapDefaultEndpoints()`. File-based AppHosts work but lose testability — prefer real `.csproj` projects.
2. **Pick the verb by type.** `AddProject<TProject>` for .NET projects with launchSettings; `AddExecutable` for arbitrary binaries; `AddContainer` for Docker images; `AddNpmApp`/`AddViteApp`/`AddYarnApp`/`AddPnpmApp` for Node frontends; `AddDockerfile` for built-from-source containers. See [apphost-wiring.md](apphost-wiring.md).
3. **Pick the startup operator by behavior.** Long-running listeners (Web API, gRPC, worker, frontend) start automatically. **One-shot CLIs that do work and exit must use `.WithExplicitStart()`** — otherwise Aspire restarts them indefinitely. See [apphost-wiring.md](apphost-wiring.md).
4. **`WithReference` AND `WaitFor`.** `WithReference` injects connection info; `WaitFor` blocks startup until the dependency is healthy. Missing `WaitFor` causes transient failures on first request. Use `WaitForCompletion` for setup/migration resources that exit, `WaitForStart` to skip the health gate.
5. **Switching between emulator and real is a single binary flag.** `UseRealInfrastructure` (or equivalent) at the top of `Program.cs`, branching once per resource. Never split into per-resource flags — the matrix explodes and bugs from mixed states never reflect a real environment. See [emulators-and-real-infra.md](emulators-and-real-infra.md).
6. **`RunAsEmulator()` is called on the parent before child resources.** The fluent API changes the builder type after child resources are added; calling `RunAsEmulator()` later breaks compilation.
7. **Log capture is in-process via `Blaztrap.Aspire.FileLogging`.** Call `builder.AddFileLogging(logsDir)` AFTER every `AddProject`/`AddExecutable` and BEFORE `Build()`. Works identically in `aspire run` and in `DistributedApplicationTestingBuilder`. See [file-logging.md](file-logging.md).
8. **Tests are owned by the `dotnet-testing` skill.** This skill produces the topology and the file-logging primitive; the test project layout, `DistributedApplicationTestingBuilder` usage, MSTest parallelism settings, `TestResults/{run-id}/...` artefact layout, and the four canonical seeding strategies all live in `dotnet-testing`. Load it before authoring or auditing any test class.

## Project-type matrix (in-scope, frequent)

Use this matrix on every `AddProject` call. The skill's longer chapters refer back to it.

| Project signature (how to recognise) | Verb | Startup | Endpoints | Notes |
|---|---|---|---|---|
| **Web API** — `Microsoft.NET.Sdk.Web`, `WebApplication.CreateBuilder`, `Map*` endpoints, exposes HTTP/HTTPS profiles | `AddProject<T>("api")` | auto | `WithHttpEndpoint` / `WithHttpsEndpoint` if the launchSettings profile lacks them | Consumers call `AddServiceDefaults()`. |
| **gRPC service** — `Microsoft.NET.Sdk.Web`, `MapGrpcService`, `Grpc.AspNetCore` package | `AddProject<T>("grpc")` | auto | `WithHttpsEndpoint` (gRPC needs HTTP/2; HTTPS or h2c) | Add `<EnableConfigurationBindingGenerator>true</EnableConfigurationBindingGenerator>` if needed; clients use service discovery (`https://grpc`). |
| **Worker / hosted service** — `Microsoft.NET.Sdk.Worker`, `AddHostedService`, no HTTP endpoints | `AddProject<T>("worker")` | auto | none | Health check optional but recommended via `MapDefaultEndpoints` on a side HTTP port. |
| **CLI / one-shot tool** — `Microsoft.NET.Sdk`, top-level statements that do work and exit, no `WebApplication` | `AddProject<T>("cli").WithExplicitStart()` | manual (dashboard "Start" or `app.ResourceNotifications.WaitForResourceAsync`) | none | Without `WithExplicitStart`, Aspire restart-loops the CLI. Use `WaitForCompletion(cli)` from a downstream resource if it must run before the rest. |
| **JS/TS frontend** — `package.json` with `vite`/`next`/`react-scripts`, served by Node | `AddViteApp("web", "../web")` / `AddNpmApp(...)` | auto | `WithHttpEndpoint(env: "PORT")` | Use `WithReference(api)` to inject `services__api__http__0` into the Node process; the frontend reads it via `import.meta.env.VITE_*` after the proxy translates. |
| **Container app** (third-party or built locally) | `AddContainer("name", "image", "tag")` / `AddDockerfile` | auto | `WithHttpEndpoint(targetPort: N)` | Lifetime: `Persistent` (survives restarts), `Session` (default, dies with the host). |

When unsure, **read the project's `Program.cs` AND `.csproj`**: SDK + presence/absence of `WebApplication`/`MapGrpcService`/`AddHostedService` is enough to disambiguate.

## Sub-index

| Topic | File | Load when |
|---|---|---|
| Scaffolding a new Aspire solution; enrolling an existing repo (Docker Compose or unstructured) | [scaffolding.md](scaffolding.md) | Greenfield setup, or migrating an existing project graph into Aspire. |
| AppHost wiring: registration verbs, fluent operators (`WithReference`, `WaitFor*`, `WithExplicitStart`), endpoint shaping per project type | [apphost-wiring.md](apphost-wiring.md) | Editing `AppHost/Program.cs`, deciding which verb/operator applies. |
| Switching between emulators / stubs and real infrastructure with a single binary flag (Categories A: native Aspire emulators, B: container stubs without native integration, C: third-party HTTP services with WireMock) | [emulators-and-real-infra.md](emulators-and-real-infra.md) | Wiring tests against multiple environments, or adding a new external dependency. |
| Per-resource and host-level file logs via `Blaztrap.Aspire.FileLogging` (works identically in `aspire run` and in `DistributedApplicationTestingBuilder`) | [file-logging.md](file-logging.md) | Capturing stdout/stderr from each resource plus AppHost/DCP/Aspire categories to disk. |
| Playwright wiring against Aspire-allocated endpoints | [playwright-testing.md](playwright-testing.md) | Authoring a UI test that hits an Aspire `app.GetEndpoint(...)` URL. |
| Symptom→cause→fix catalogue (~20 problems across boot, connectivity, runtime, deploy) | [troubleshooting.md](troubleshooting.md) | An Aspire run is failing or behaving unexpectedly. |
| `aspire publish` / `aspire deploy`, manifest format, `PublishAs*`, `azd` integration, Compose + K8s publishers | [publish-deploy.md](publish-deploy.md) | Publishing or deploying an Aspire solution. |

## Quick decision matrix

| Need | Pick |
|---|---|
| Brand-new Aspire solution | `aspire new` (template `aspire-starter`) — see [scaffolding.md](scaffolding.md). |
| Existing repo with `docker-compose.yml` | **Default: translate once and delete `docker-compose.yml`** — map every service to `AddContainer`/`AddDockerfile`/`AddProject` and replace `docker compose up` with `aspire run`. Only keep `AddDockerComposePublisher` for round-trip when the user explicitly asks (other tooling still consumes the compose file). See [scaffolding.md](scaffolding.md) § Compose enrollment. |
| Existing repo without orchestrator | New `AppHost` + `ServiceDefaults` projects, then register each existing project per the project-type matrix above — see [scaffolding.md](scaffolding.md) § Greenfield enrollment. |
| New external HTTP API with no Aspire integration | `AddExternalService` for service discovery; in tests, swap to `AddContainer("wiremock", ...)` behind the binary flag — see [emulators-and-real-infra.md](emulators-and-real-infra.md). |
| Cosmos / Service Bus / Storage / SQL / Redis | Native Aspire integration with `RunAsEmulator()` for local + `AsExisting`/`AddConnectionString` for real — see [emulators-and-real-infra.md](emulators-and-real-infra.md). |
| AWS S3/SQS/DynamoDB | Local: `AddContainer("localstack", "localstack/localstack")` + composite connection string; real: `AddConnectionString("aws")` — see [emulators-and-real-infra.md](emulators-and-real-infra.md). |
| Per-resource log files for exploratory runs OR tests | `builder.AddFileLogging(logsDir)` from `Blaztrap.Aspire.FileLogging` — see [file-logging.md](file-logging.md). |
| First MSTest integration test | Load `dotnet-testing` — single `Company.Product.Test` project, per-class `DistributedApplicationTestingBuilder.CreateAsync` mount. |
| Seed test data into a stateful resource | Load `dotnet-testing` — four canonical strategies under the ephemeral-always invariant. |
| UI test against an Aspire-orchestrated frontend | Override `PageTest.ContextOptions().BaseURL = App.GetEndpoint("web").ToString()` — see [playwright-testing.md](playwright-testing.md). |
| Aspire run is failing or flaky | Symptom-cause-fix table grouped by boot / connectivity / runtime / deploy — see [troubleshooting.md](troubleshooting.md). |
| Publish or deploy (ACA via `azd`, Compose, k8s) | `aspire publish --publisher <azd\|docker-compose\|kubernetes>` then `aspire deploy` or `azd up` — see [publish-deploy.md](publish-deploy.md). |

## Cross-references

- Live (Aspire overview): https://learn.microsoft.com/en-us/dotnet/aspire/get-started/aspire-overview
- Live (AppHost + integrations): https://learn.microsoft.com/en-us/dotnet/aspire/fundamentals/app-host-overview
- Live (testing): https://learn.microsoft.com/en-us/dotnet/aspire/testing/manage-app-host
- Live (`DistributedApplicationTestingBuilder.CreateAsync`): https://learn.microsoft.com/en-us/dotnet/api/aspire.hosting.testing.distributedapplicationtestingbuilder.createasync
- Live (`AddExternalService`): https://learn.microsoft.com/en-us/dotnet/api/aspire.hosting.externalservicebuilderextensions.addexternalservice
- Live (Cosmos DB integration with `AsExisting`): https://learn.microsoft.com/en-us/dotnet/aspire/database/azure-cosmos-db-integration
- Live (local Azure provisioning): https://learn.microsoft.com/en-us/dotnet/aspire/azure/local-provisioning
- Live (Docker Compose publisher / AppHost): https://learn.microsoft.com/en-us/dotnet/aspire/deployment/docker-compose
- Related skill: `dotnet-hexagonal-architecture` — owns the team's canonical solution layout, project breakdown (`Core/Host/Infrastructure`), shared `Command`/`Result`/`Event` bases, app-wide `ErrorCode` enum. When in doubt about *what* to wire, consult that skill; this skill owns *how* to wire it with Aspire.
- Related skill: `dotnet-testing` — single `Company.Product.Test` project, per-class `DistributedApplicationTestingBuilder` mount, MSTest parallelism settings, file-logging integration in tests, four canonical seeding strategies, testing-related forbidden patterns.
- Related skill: `dotnet-file-based-apps` — when an AppHost or consumer is a single `.cs` file.
- Related skill: `dotnet-system-commandline` — designing the CLI surface of an executable resource.
- Related skill: `dotnet-scripting` — combined recipe for `.cs` + `System.CommandLine`.
