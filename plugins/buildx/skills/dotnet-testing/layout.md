# Test Project Layout

Single-project layout, surface folders, and the file/class/method naming conventions every test on this team follows.

## Project naming and physical location

```
test/
  Acme.Inventory/
    Acme.Inventory.Test/
      Acme.Inventory.Test.csproj
      AssemblyInfo.cs
      AppHostFixture.cs        # the ONE canonical fixture base — see mstest-integration.md
      TestArtifacts.cs         # the ONE artefact-path helper — see mstest-integration.md
      TestSettings.cs          # the ONE run-wiring file (TESTRUN_* env vars) — see mstest-integration.md
      HTTP/
      UI/
      Grpc/
      Cli/
      Worker/
      Queue/
      Webhook/
      Seeding/
      TestData/
```

- **Singular `Test`.** `Acme.Inventory.Test`, never `.Tests` (plural), `.UnitTests`, `.IntegrationTests`, `.E2ETests`, `.WebTests`, `.Smoke`, `.Acceptance`.
- **One project per product.** No `.Test.Web` / `.Test.Api` siblings; surface separation is folder-level inside the single project.
- **Lives under `test/[Company].[Product]/`** at the same depth as `src/[Company].[Product]/`, never under `src/`.

## `.csproj` shape

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Aspire.Hosting.Testing" Version="13.2.*" />
    <PackageReference Include="MSTest" Version="4.*" />
    <PackageReference Include="Microsoft.Extensions.Http.Resilience" Version="9.*" />
    <PackageReference Include="Blaztrap.Aspire.FileLogging" Version="*" />
  </ItemGroup>

  <!-- BANNED here: Moq / NSubstitute / FakeItEasy / WireMock.Net, and the legacy
       Blaztrap.Aspire.Testing.FileLogging (drops apphost.log — use Blaztrap.Aspire.FileLogging). -->

  <ItemGroup>
    <ProjectReference Include="..\..\..\src\Acme.Inventory\Acme.Inventory.AppHost\Acme.Inventory.AppHost.csproj" />
  </ItemGroup>

  <ItemGroup>
    <None Update="TestData\**\*" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

Bootstrap from the official Aspire-MSTest template, then rename and re-home:

```powershell
dotnet new aspire-mstest -n Acme.Inventory.Test -o test/Acme.Inventory/Acme.Inventory.Test
dotnet add test/Acme.Inventory/Acme.Inventory.Test reference src/Acme.Inventory/Acme.Inventory.AppHost
```

**Banned packages** (greppable enforcement): `Moq`, `NSubstitute`, `FakeItEasy`, `WireMock.Net`, `Blaztrap.Aspire.Testing.FileLogging`. See [forbidden-patterns.md](forbidden-patterns.md).

## Surface folders — derived from the AppHost's executables

The folder set is **not free-form**: it is derived from the executable resources the AppHost declares (`AddProject` / `AddExecutable`, cross-checked against the apps listed in `docs/SOLUTION.md`). Pick the folder that matches **how the test reaches the system under test** — which is always one of those executables' real surfaces, never an application layer.

| Folder | Exists when the AppHost declares… | What lives here |
|---|---|---|
| `HTTP/` | An API service, or a web app exposing HTTP endpoints. | Public HTTP API exercised through `App.CreateHttpClient("api")`. |
| `Grpc/` | A gRPC service. | Generated clients pointed at `App.GetEndpoint("grpc")`. |
| `UI/` | A browser-served web app. | Playwright tests. One file per page or feature. |
| `Cli/` | A CLI executable. | The CLI run as a child process (or via its Aspire resource); assert stdout / stderr / exit code / side effects. |
| `Worker/` | A hosted-service / background app with a timer or hosted trigger. | Trigger the input, wait for the observable effect. |
| `Queue/` | Any executable consuming a message bus. | Publish a real message, wait for the side effect (DB row, outbound HTTP, second queue). |
| `Webhook/` | An executable receiving inbound webhooks. | Driven via the stub project's outbound call. |

Rules:

- **A folder with no matching executable is a defect.** If only a web UI and a CLI exist, the test project contains `UI/` and `Cli/` (plus `HTTP/` only if the web app exposes an API the tests exercise) — nothing else.
- **There is no `Service/` folder.** "Application service exercised in-process" is a unit test wearing a costume — see [forbidden-patterns.md](forbidden-patterns.md) § in-process guard tests. If a behaviour has no executable surface, the missing surface is the finding (escalate to `dotnet-architect` / `analyst`), not a new folder.
- Create the folder when you write the first test for that surface; leave the others off disk.

## File / class / method naming

- **One `[TestClass]` per area.** File name: `{Area}_Tests.cs` (e.g. `Orders_Tests.cs`, `Payments_Tests.cs`, `StripeWebhook_Tests.cs`). Class name matches the file: `public class Orders_Tests`.
- **Method names: `{Action}_{Scenario}_{Expectation}`**. PascalCase action and expectation; the scenario is whatever phrase reads cleanest. Examples:
  - `CreateOrder_WithValidData_ReturnsCreated`
  - `CreateOrder_WhenCustomerMissing_ReturnsNotFound`
  - `ListOrders_WhenPageTooLarge_ReturnsBadRequest`
- **No magic numbers in names.** `SendInvoice_WhenAmountIsZero_ReturnsAccepted` beats `SendInvoice_Test_3`.

## `AssemblyInfo.cs`

Required content for any Aspire-based test project:

```csharp
using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: Parallelize(Workers = 1, Scope = ExecutionScope.MethodLevel)]
```

See [mstest-integration.md](mstest-integration.md) § Parallelism for the rationale.

## Banned variants

- `Acme.Inventory.Tests` — singular `.Test`, never plural.
- `Acme.Inventory.UnitTests` — there are no unit tests on this team.
- `Acme.Inventory.E2ETests` — UI tests live in `UI/` inside the single project.
- `Acme.Inventory.IntegrationTests` — the project is already integration-only; the suffix is redundant.
- `Acme.Inventory.Test.Web` / `Acme.Inventory.Test.Api` — those are folders, not projects.
- A second test project for "slow" or "flaky" tests — fix the test instead.
- `[AssemblyInitialize]` mounting the system-under-test AppHost. `[AssemblyInitialize]` for non-AppHost suite-wide setup (Playwright auth state, pre-computing reference data) IS allowed.
- A second fixture base, or a `[TestClass]` that builds its own `DistributedApplication` outside `AppHostFixture` — ONE base owns the lifecycle; every system-exercising class inherits it.
- A `Service/` folder, or any folder with no matching AppHost executable (`Authorization/`, `Hosting/`, `Notifications/`, …) — see § Surface folders.

## Enforcement

```powershell
# Exactly one *.Test.csproj path
gci -Recurse -Filter *.csproj | Select-String -Pattern "MSTest|Microsoft.NET.Test.Sdk" -List | % Path
# Must match the singular .Test pattern
gci -Recurse -Filter *.csproj | ? Name -Match "\.Tests?\.csproj$" | % FullName
# No mocking libraries, no legacy logging package
gci -Recurse -Filter *.csproj | Select-String "Moq|NSubstitute|FakeItEasy|WireMock|Blaztrap.Aspire.Testing.FileLogging"
# Exactly one fixture: every CreateAsync<...AppHost> call lives in AppHostFixture.cs
gci -Recurse -Filter *.cs -Path test | Select-String "DistributedApplicationTestingBuilder" | % Path | sort -Unique
# No legacy logging API, no path-override env vars, no in-repo artefact roots
gci -Recurse -Filter *.cs -Path test | Select-String "AddResourceFileLogging|BLAZTRAP_TEST_RUN_DIR|_LOG_DIR"
# Env vars are read in TestSettings.cs ONLY
gci -Recurse -Filter *.cs -Path test | Select-String "GetEnvironmentVariable" | ? Path -NotMatch "TestSettings\.cs$"
# No hand-rolled fakes: test classes implementing production interfaces (review every hit)
gci -Recurse -Filter *.cs -Path test | Select-String "(class|record)\s+\w+\s*:\s*I[A-Z]\w+(Store|Repository|Client|Sender|Service|Provider|Publisher)"
```

Any plural-`.Tests` match, any second `.Test.csproj`, any banned package, any `DistributedApplicationTestingBuilder` outside `AppHostFixture.cs`, or any legacy-logging / env-var / hand-rolled-fake hit is a blocking finding.

## Cross-references

- [mstest-integration.md](mstest-integration.md) — canonical `AppHostFixture`, parallelism, file logging, artefact layout.
- [seeding.md](seeding.md) — what goes under `Seeding/` and `TestData/`.
- [forbidden-patterns.md](forbidden-patterns.md) — bans on third-party mocks and on production-side test branches.
- Sibling skill: `dotnet-hexagonal-architecture` § Project breakdown — where the surface taxonomy comes from.
