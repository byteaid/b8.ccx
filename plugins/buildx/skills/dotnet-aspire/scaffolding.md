# Scaffolding & Enrollment

Two paths:

1. **Greenfield** — `aspire new` from scratch, or add `AppHost` + `ServiceDefaults` to a brand-new solution.
2. **Enrollment** — bring an existing repo into Aspire. Two flavours: from `docker-compose.yml`, or from a project graph with no orchestrator at all.

Both end at the same shape: an `AppHost` project that wires every component, a `ServiceDefaults` class library every consumer references, and a flat top-level solution that includes both.

## 1. Prerequisites

- **.NET 10 SDK** (Aspire 13.2 targets `net10.0`).
- **Aspire CLI**: `dotnet tool install --global Aspire.Cli` (or `dotnet workload install aspire` on older setups; Aspire 13.x prefers the CLI tool).
- **Docker** running locally (for emulators, containers, and DCP-managed runtimes).
- **Templates**: `dotnet new install Aspire.ProjectTemplates::13.2.*` if not already present.

Verify:

```bash
dotnet --list-sdks                    # 10.0.x or higher
aspire --version                      # 13.2.x
docker info                           # daemon reachable
```

## 2. Greenfield: `aspire new`

The shortest path. Creates a `.sln`, `AppHost`, `ServiceDefaults`, and one Web API in one shot.

```bash
mkdir Contoso.Foo && cd Contoso.Foo
aspire new aspire-starter --name Contoso.Foo
# or, equivalently with dotnet:
# dotnet new aspire-starter -n Contoso.Foo
```

Result:

```
Contoso.Foo/
  Contoso.Foo.sln
  src/
    Contoso.Foo.AppHost/
      Contoso.Foo.AppHost.csproj
      Program.cs
      appsettings.json
    Contoso.Foo.ServiceDefaults/
      Contoso.Foo.ServiceDefaults.csproj
      Extensions.cs
    Contoso.Foo.ApiService/
      Contoso.Foo.ApiService.csproj
      Program.cs
    Contoso.Foo.Web/                       # Blazor by default; remove if not needed
      ...
```

Other useful templates:

| Template | Result |
|---|---|
| `aspire` | Empty AppHost + ServiceDefaults only. |
| `aspire-starter` | AppHost + ServiceDefaults + Web API + Blazor frontend. |
| `aspire-apphost` | Single AppHost project (add to an existing solution). |
| `aspire-servicedefaults` | Single ServiceDefaults class library. |
| `aspire-mstest` / `aspire-xunit` / `aspire-nunit` | Test project pre-wired to `DistributedApplicationTestingBuilder`. |

Run:

```bash
aspire run        # equivalent to: dotnet run --project src/Contoso.Foo.AppHost
```

The dashboard URL is printed to stdout (default `https://localhost:17109`). Open it to see the resource graph.

## 3. The `AppHost.csproj` shape

Reference shape for an AppHost added by hand:

```xml
<Project Sdk="Aspire.AppHost.Sdk/13.2.0">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UserSecretsId>contoso-foo-apphost</UserSecretsId>
    <IsAspireHost>true</IsAspireHost>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Aspire.Hosting.AppHost" Version="13.2.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Contoso.Foo.ApiService\Contoso.Foo.ApiService.csproj" />
    <ProjectReference Include="..\Contoso.Foo.Worker\Contoso.Foo.Worker.csproj" />
  </ItemGroup>
</Project>
```

Notes:
- `Aspire.AppHost.Sdk` (custom MSBuild SDK) generates the strongly-typed `Projects.*` references the AppHost uses (`Projects.Contoso_Foo_ApiService`).
- `IsAspireHost=true` is what makes the dashboard / DCP recognise the project.
- Every consumer project added as a `ProjectReference` here becomes available as `Projects.{Sanitized_Name}` (dots → underscores).

## 4. The `ServiceDefaults` shape

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsAspireSharedProject>true</IsAspireSharedProject>
  </PropertyGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    <PackageReference Include="Microsoft.Extensions.Http.Resilience" Version="9.*" />
    <PackageReference Include="Microsoft.Extensions.ServiceDiscovery" Version="13.2.*" />
    <PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.*" />
    <PackageReference Include="OpenTelemetry.Extensions.Hosting" Version="1.*" />
    <PackageReference Include="OpenTelemetry.Instrumentation.AspNetCore" Version="1.*" />
    <PackageReference Include="OpenTelemetry.Instrumentation.Http" Version="1.*" />
    <PackageReference Include="OpenTelemetry.Instrumentation.Runtime" Version="1.*" />
  </ItemGroup>
</Project>
```

`Extensions.cs` exposes the canonical `AddServiceDefaults` / `MapDefaultEndpoints` pair:

```csharp
public static class Extensions
{
    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();
        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });
        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.MapHealthChecks("/health");
            app.MapHealthChecks("/alive", new() { Predicate = r => r.Tags.Contains("live") });
        }
        return app;
    }
    // … ConfigureOpenTelemetry / AddDefaultHealthChecks elsewhere
}
```

Consumer wiring (uniform across Web API, gRPC, worker, CLI):

```csharp
var builder = WebApplication.CreateBuilder(args);     // or Host.CreateApplicationBuilder
builder.AddServiceDefaults();                          // <- always
// ... rest of Program.cs ...
var app = builder.Build();
app.MapDefaultEndpoints();                             // <- only Web/gRPC; workers/CLIs skip
```

## 5. Enrollment from Docker Compose

**Default strategy: translate once and delete `docker-compose.yml` (§ 5.1).** Aspire's resource model is the new source of truth; keeping a parallel compose file means two declarations to maintain in lockstep, and they drift. Apply § 5.2 (round-trip via `AddDockerComposePublisher`) **only when the user explicitly asks** — typically because other tooling (CI scripts, deploy targets, a different team) still consumes the compose file and cannot be migrated yet.

### 5.1 Translate once, drop Compose (default)

Procedure:

1. List every service in `docker-compose.yml`. Map each to one of:
   - **`AddProject<T>(...)`** if the service builds a .NET project in this repo.
   - **`AddContainer("name", "image", "tag")`** for third-party images (databases, brokers, stubs).
   - **`AddDockerfile("name", "../path", "Dockerfile")`** for first-party services that build from a Dockerfile in the repo.
2. Translate `depends_on:` into `WithReference(other).WaitFor(other)` chains.
3. Translate `environment:` into `WithEnvironment("KEY", "value")` (literal) or `WithEnvironment("KEY", resourceRef)` (reference) calls.
4. Translate `ports:` into `WithHttpEndpoint(port: H, targetPort: C)` or `WithEndpoint(...)`.
5. Translate `volumes:` into `WithBindMount("host-path", "container-path", isReadOnly: bool)` or `WithVolume("named-volume", "container-path")`.
6. Replace `docker compose up` with `aspire run`. Delete `docker-compose.yml` once parity is verified.

Worked example:

```yaml
# Before — docker-compose.yml
services:
  api:
    build: ./src/Api
    ports: ["5000:8080"]
    depends_on: [postgres]
    environment:
      ConnectionStrings__db: "Host=postgres;Database=app;..."
  postgres:
    image: postgres:16
    environment:
      POSTGRES_PASSWORD: dev
    volumes:
      - pgdata:/var/lib/postgresql/data
volumes:
  pgdata:
```

```csharp
// After — AppHost/Program.cs
var builder = DistributedApplication.CreateBuilder(args);

var pg = builder.AddPostgres("postgres")
    .WithDataVolume("pgdata")
    .WithLifetime(ContainerLifetime.Persistent);

var db = pg.AddDatabase("db");

builder.AddProject<Projects.Api>("api")
    .WithReference(db).WaitFor(db)
    .WithHttpEndpoint(port: 5000, targetPort: 8080);

builder.Build().Run();
```

The Postgres password becomes a generated parameter (Aspire injects via `ConnectionStrings__db`); the consumer's `Program.cs` calls `builder.AddNpgsqlDataSource("db")` and reads it transparently.

### 5.2 Round-trip with `AddDockerComposePublisher` (only on explicit request)

Use this only when the user instructs you to keep the compose file alive — e.g. external CI / staging consumes `docker-compose.yml` and migrating that pipeline is out of scope. Otherwise prefer § 5.1.

```csharp
var builder = DistributedApplication.CreateBuilder(args);

builder.AddDockerComposePublisher("compose")
    .WithProperties(p => p.OutputPath = "../docker-compose.yml");

// ...declare resources normally...

builder.Build().Run();
```

`aspire publish` then writes `../docker-compose.yml` from the in-memory model. Useful when CI / staging uses Compose but local dev uses Aspire.

## 6. Enrollment from a project graph (no orchestrator)

The repo already has multiple projects but is started by hand (`dotnet run --project Foo`, `npm run dev`, etc.). Goal: introduce Aspire without rewriting the consumers.

Procedure:

1. **Add the AppHost + ServiceDefaults projects** (templates `aspire-apphost` + `aspire-servicedefaults`):

   ```bash
   dotnet new aspire-apphost -n Contoso.Foo.AppHost -o src/Contoso.Foo.AppHost
   dotnet new aspire-servicedefaults -n Contoso.Foo.ServiceDefaults -o src/Contoso.Foo.ServiceDefaults
   dotnet sln add src/Contoso.Foo.AppHost src/Contoso.Foo.ServiceDefaults
   ```

2. **Reference every existing project from the AppHost.** Each `<ProjectReference>` becomes a `Projects.*` typed identifier:

   ```xml
   <ItemGroup>
     <ProjectReference Include="..\Contoso.Foo.Api\Contoso.Foo.Api.csproj" />
     <ProjectReference Include="..\Contoso.Foo.Worker\Contoso.Foo.Worker.csproj" />
     <ProjectReference Include="..\Contoso.Foo.Migrations\Contoso.Foo.Migrations.csproj" />
   </ItemGroup>
   ```

3. **Wire each project per the matrix** in [skill.md](skill.md) § Project-type matrix. Read the project's `Program.cs` AND `.csproj` to disambiguate:

   ```csharp
   var builder = DistributedApplication.CreateBuilder(args);

   var api = builder.AddProject<Projects.Contoso_Foo_Api>("api");                  // Web API → auto-start
   var worker = builder.AddProject<Projects.Contoso_Foo_Worker>("worker");          // hosted service → auto-start
   var migrations = builder.AddProject<Projects.Contoso_Foo_Migrations>("migrations")
       .WithExplicitStart();                                                        // CLI → explicit
   ```

4. **Add `AddServiceDefaults()` to each consumer's `Program.cs`.** This is the only edit the consumers need. For Web/gRPC also add `app.MapDefaultEndpoints()` after `Build()`.

5. **Add `<ProjectReference Include="...\Contoso.Foo.ServiceDefaults.csproj" />`** to every consumer `.csproj` that calls `AddServiceDefaults`.

6. **Move external dependencies into the AppHost.** Anything the app needs (Postgres, Redis, an SMTP server, a stub of a partner API) becomes an `AddContainer` / `AddPostgres` / `AddRedis` / `AddExternalService` call. Then `WithReference(...)` from each consumer that needs it.

7. **Run.** `aspire run` from the repo root, or `dotnet run --project src/Contoso.Foo.AppHost`.

## 7. JS/TS frontends

Vite/Next/React frontends served by Node use the dedicated verbs:

```csharp
var api = builder.AddProject<Projects.Contoso_Foo_Api>("api")
    .WithHttpEndpoint(name: "http");

builder.AddViteApp("web", workingDirectory: "../../web", packageManager: "pnpm")
    .WithReference(api)
    .WithHttpEndpoint(env: "PORT")
    .WithExternalHttpEndpoints();
```

Caveats:
- The frontend process reads service-discovery env vars exposed under `services__api__http__0`. Vite proxies prefer reading the API URL through `VITE_API_URL`, so add `.WithEnvironment("VITE_API_URL", api.GetEndpoint("http"))` for ergonomic consumer code.
- `AddNpmApp(...)` is the generic verb when you don't use Vite.
- `WithExternalHttpEndpoints()` makes the endpoint reachable from outside the Aspire-managed network (browsers).

## 8. Verification checklist

- [ ] `aspire run` brings up the dashboard.
- [ ] Every project shows `Running` / `Healthy` in the dashboard.
- [ ] No project shows infinite restart loops (likely cause: a CLI without `WithExplicitStart`).
- [ ] Service discovery resolves: hitting the API from another consumer with `https://api` works.
- [ ] Deleting `docker-compose.yml` (if migrating) leaves nothing referenced from CI/build scripts.
- [ ] Every consumer calls `AddServiceDefaults()` and references the `ServiceDefaults` project.

## Cross-references

- Live (templates): https://learn.microsoft.com/en-us/dotnet/aspire/fundamentals/aspire-sdk-templates
- Live (AppHost overview): https://learn.microsoft.com/en-us/dotnet/aspire/fundamentals/app-host-overview
- Live (ServiceDefaults): https://learn.microsoft.com/en-us/dotnet/aspire/fundamentals/service-defaults
- Live (Docker Compose support): https://learn.microsoft.com/en-us/dotnet/aspire/deployment/docker-compose
- Live (`AddViteApp` / `AddNpmApp`): https://learn.microsoft.com/en-us/dotnet/aspire/javascript/nodejs
- Sibling: [apphost-wiring.md](apphost-wiring.md) — what to write inside the AppHost once the projects exist.
- Sibling: [emulators-and-real-infra.md](emulators-and-real-infra.md) — wiring external dependencies behind a switch.
