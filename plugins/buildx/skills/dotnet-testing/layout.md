# Test Project Layout

Single-project layout, surface folders, and the file/class/method naming conventions every test on this team follows.

## Project naming and physical location

```
test/
  Acme.Inventory/
    Acme.Inventory.Test/
      Acme.Inventory.Test.csproj
      AssemblyInfo.cs
      HTTP/
      UI/
      gRPC/
      Service/
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
    <PackageReference Include="MSTest.TestFramework" Version="3.6.*" />
    <PackageReference Include="MSTest.TestAdapter"  Version="3.6.*" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="Microsoft.Extensions.Http.Resilience" Version="9.*" />
    <PackageReference Include="Blaztrap.Aspire.FileLogging" Version="*" />
  </ItemGroup>

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

**Banned packages** (greppable enforcement): `Moq`, `NSubstitute`, `FakeItEasy`, `WireMock.Net`. See [forbidden-patterns.md](forbidden-patterns.md).

## Surface folders

Folders correspond to the surface area being tested. Pick the one that matches **how the test reaches the system under test**, not the application layer the code lives in.

| Folder | What lives here |
|---|---|
| `HTTP/` | REST controllers, `Map<Controller>` endpoints, public HTTP API exercised through `App.CreateHttpClient("api")`. |
| `gRPC/` | gRPC services exercised through generated clients pointed at `App.GetEndpoint("grpc")`. |
| `UI/` | Browser-driven tests (Playwright). One file per page or feature. |
| `Service/` | Application services exercised in-process *via the host*, not via DI lookup — typically when no transport boundary exists yet. |
| `Worker/` | Hosted services / `IHostedService` implementations exercised by triggering their input (queue, timer, message). |
| `Queue/` | Message-driven flows: enqueue a message, wait for the side effect (DB row, outbound HTTP, second queue). |
| `Webhook/` | Inbound webhooks (Stripe, GitHub, partner systems). Driven via the stub project's outbound call. |

A single product almost never uses every folder — create the folder when you write the first test for that surface, leave the others off disk.

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
- `[AssemblyInitialize]` mounting a shared AppHost — discipline is per-class mount.
- An inherited `AppHostFixtureBase` shared between classes — same reason.

## Enforcement

```powershell
# Exactly one *.Test.csproj path
gci -Recurse -Filter *.csproj | Select-String -Pattern "MSTest.Sdk|Microsoft.NET.Test.Sdk" -List | % Path
# Must match the singular .Test pattern
gci -Recurse -Filter *.csproj | ? Name -Match "\.Tests?\.csproj$" | % FullName
# No mocking libraries
gci -Recurse -Filter *.csproj | Select-String "Moq|NSubstitute|FakeItEasy|WireMock"
```

Any plural-`.Tests` match, any second `.Test.csproj`, or any banned package match is a blocking finding.

## Cross-references

- [mstest-integration.md](mstest-integration.md) — per-class mount, parallelism, file logging, artefact layout.
- [seeding.md](seeding.md) — what goes under `Seeding/` and `TestData/`.
- [forbidden-patterns.md](forbidden-patterns.md) — bans on third-party mocks and on production-side test branches.
- Sibling skill: `dotnet-hexagonal-architecture` § Project breakdown — where the surface taxonomy comes from.
