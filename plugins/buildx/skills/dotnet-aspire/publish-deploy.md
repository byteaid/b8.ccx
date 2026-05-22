# Publish and Deploy

How `aspire publish` produces deployment artifacts, how `aspire deploy` and `azd` apply them, and which knobs (`PublishAsDockerfile`, `PublishAsAzureContainerApp`, secrets, environments) shape the output. Companion to [scaffolding.md](scaffolding.md) (which owns greenfield + Compose enrollment).

## Mental model

Aspire separates two phases:

1. **Publish** — convert the AppHost graph into a static artifact: JSON manifest, Bicep, `docker-compose.yml`, or k8s YAML. Driven by `aspire publish --publisher <name>`.
2. **Deploy** — apply the artifact to the target. Either `aspire deploy` (integrated) or an external tool (`azd up`, `docker compose up`, `kubectl apply`).

Since 13.x the two steps are decoupled by design — you can publish without deploying, commit the artifact, and let CI / GitOps apply it later.

## Publishers (Aspire 13.2)

| Publisher | Target | Package |
|---|---|---|
| **Azure Container Apps** | ACA + Azure resources (SQL, Storage, etc.) | `Aspire.Hosting.Azure.AppContainers` (consumed by `azd`) |
| **Docker Compose** | `docker-compose.yml` + containers | `Aspire.Hosting.Docker` (stable since 13.2) |
| **Kubernetes** | k8s manifests (Deployments, Services, ConfigMaps, Secrets) | `Aspire.Hosting.Kubernetes` |
| **Manifest** | Generic JSON for downstream tools | Built-in |

## `aspire publish` — basic commands

```bash
cd Contoso.Foo.AppHost

# Generic manifest
aspire publish --publisher manifest       --output-path ./out

# Bicep + azure.yaml for azd
aspire publish --publisher azd            --output-path ./infra

# docker-compose.yml + .env
aspire publish --publisher docker-compose --output-path ./compose

# Deployments / Services / ConfigMaps / Secrets
aspire publish --publisher kubernetes     --output-path ./k8s
```

What lands per publisher:

| Publisher | Output |
|---|---|
| `manifest` | `aspire-manifest.json` — full resource graph with placeholder values |
| `azd` | Bicep modules in `./infra` + `azure.yaml` consumable by `azd deploy` |
| `docker-compose` | `docker-compose.yml` + a `.env` for parameter values |
| `kubernetes` | One file per resource (`deployment-*.yaml`, `service-*.yaml`, `configmap-*.yaml`, `secret-*.yaml`) + `ingress.yaml` for `WithExternalHttpEndpoints()` |

## `aspire deploy` vs `azd up`

```bash
# Aspire integrated pipeline (publish + apply in one step for the configured target)
aspire deploy
aspire deploy --dry-run        # show what would happen
aspire deploy --env staging

# Azure-specific, more mature path
azd up                          # provision + deploy
```

`aspire deploy` is opinionated and less flexible than `azd`. For Azure deployments, `azd` remains the more robust path:

```bash
cd Contoso.Foo.AppHost
azd init                    # one-time
azd auth login
azd env new production
azd provision               # runs Bicep → creates Azure resources
azd deploy                  # builds + pushes containers to ACR + updates ACA
azd up                      # provision + deploy combined
```

`azd` reads `azure.yaml` and the Aspire-generated manifest; Bicep customisation goes through `PublishAsAzureContainerApp` (see below) — never edit generated Bicep by hand, it's regenerated on every `publish`.

**Important:** `aspire deploy` requires Docker running even when the target is ACA — it builds and pushes images client-side.

## Manifest format

Simplified `aspire-manifest.json`:

```json
{
  "resources": {
    "sql": {
      "type": "container.v0",
      "image": "mcr.microsoft.com/mssql/server:2022-latest",
      "env": {
        "ACCEPT_EULA": "Y",
        "MSSQL_SA_PASSWORD": "{sql-password.value}"
      },
      "bindings": {
        "tcp": { "scheme": "tcp", "protocol": "tcp", "transport": "tcp", "targetPort": 1433 }
      }
    },
    "appdb": {
      "type": "value.v0",
      "connectionString": "Server={sql.bindings.tcp.host},{sql.bindings.tcp.port};Database=appdb;User Id=sa;Password={sql-password.value};TrustServerCertificate=True"
    },
    "api": {
      "type": "project.v0",
      "path": "../Api/Api.csproj",
      "env": {
        "OTEL_EXPORTER_OTLP_ENDPOINT": "{aspire-dashboard.bindings.otlp.url}",
        "ConnectionStrings__appdb": "{appdb.connectionString}",
        "services__billing__http__0": "{billing.bindings.http.url}"
      },
      "bindings": {
        "http":  { "scheme": "http",  "protocol": "tcp", "transport": "http" },
        "https": { "scheme": "https", "protocol": "tcp", "transport": "http" }
      }
    }
  },
  "parameters": {
    "sql-password": { "type": "parameter.v0", "secret": true }
  }
}
```

Placeholders (`{sql-password.value}`, `{api.bindings.http.url}`) are resolved at deploy time — the target publisher (Bicep / Compose / k8s) substitutes concrete values.

## `PublishAsDockerfile`

By default, .NET projects publish as SDK-built images (`dotnet publish /t:PublishContainer`). Use `PublishAsDockerfile` only when:

- The project needs native dependencies absent from SDK images (C libraries, external CLI tools).
- A specific base image is required (`ubuntu:24.04` + `apt`).
- The team owns the Dockerfile and doesn't want Aspire to generate one.

```csharp
builder.AddProject<Projects.Api>("api").PublishAsDockerfile();

// Custom path / stage:
builder.AddProject<Projects.Api>("api")
    .PublishAsDockerfile(configure =>
    {
        configure.Path  = "./docker/Api.Dockerfile";
        configure.Stage = "runtime";
    });
```

**Watch out:** `azd` deletes `obj/aspire-manifest-*` after reading it, and Aspire 13 sometimes places dynamic Dockerfiles there → "directory not found" on build. Keep Dockerfiles outside `obj/`. See [troubleshooting.md](troubleshooting.md) E1.

## `PublishAsAzureContainerApp` — customise generated Bicep

Mutate the ACA Bicep resource (ingress, scaling, identity, secrets):

```csharp
using Azure.Provisioning.AppContainers;

builder.AddProject<Projects.Api>("api")
    .PublishAsAzureContainerApp((infra, containerApp) =>
    {
        containerApp.Configuration.Ingress = new ContainerAppIngressConfiguration
        {
            External     = true,
            TargetPort   = 8080,
            Transport    = ContainerAppIngressTransportMethod.Http,
            AllowInsecure = false,
        };

        containerApp.Template.Scale = new ContainerAppScale
        {
            MinReplicas = 1,
            MaxReplicas = 10,
            Rules =
            {
                new ContainerAppScaleRule("http")
                {
                    Http = new ContainerAppHttpScaleRule
                    {
                        Metadata = { ["concurrentRequests"] = "50" }
                    }
                }
            }
        };

        containerApp.Identity = new ManagedServiceIdentity
        {
            SystemAssignedIdentity = new SystemAssignedServiceIdentity()
        };
    });
```

Namespace: `Azure.Provisioning.AppContainers` (from `Aspire.Hosting.Azure.AppContainers`). The callback receives:

- `infra` — the `AzureBicepModuleInfrastructure`; add extra resources here.
- `containerApp` — the `ContainerApp` Bicep resource; mutate configuration.

Changes apply only to the Bicep generated on `aspire publish` — they do not affect `aspire run`.

## Secrets

Parameters declared with `secret: true` translate to:

| Target | Output |
|---|---|
| Bicep (Azure) | `@secure() param sql_password string` — value requested via `azd env set sql-password "..."`, persisted in Key Vault. |
| Docker Compose | `secrets:` section pointing at a file (`./secrets/sql-password.txt`). |
| Kubernetes | `apiVersion: v1, kind: Secret`, base64-encoded `data:`. |

In the AppHost:

```csharp
var pwd = builder.AddParameter("sql-password", secret: true);
var sql = builder.AddSqlServer("sql", password: pwd);
```

**Never** pass secrets as positional CLI args (`WithArgs("--password", pwd)`) — Bicep refuses to literalise them. Use `WithEnvironment("PASSWORD", pwd)` instead.

## Environments / `azd env`

```bash
azd env new staging
azd env new production
azd env select production
azd env set sql-password "$PROD_SQL_PASSWORD"
azd provision
azd deploy
```

Each env owns `.azure/<env-name>/.env` with resolved values (do not commit secrets).

The Aspire AppHost can branch on `builder.ExecutionContext`:

```csharp
if (builder.ExecutionContext.IsPublishMode)
{
    builder.AddAzureKeyVault("secrets");                              // real Azure
}
else
{
    builder.AddConnectionString("secrets",
        "https://dev-kv.vault.azure.net/");                            // dev / fake
}
```

`IsRunMode` for the inverse case. Use sparingly — every fork between dev and publish is a place where bugs hide. Prefer the single binary flag pattern from [emulators-and-real-infra.md](emulators-and-real-infra.md).

## Docker Compose publisher

```csharp
var sql = builder.AddSqlServer("sql").AddDatabase("appdb");
var api = builder.AddProject<Projects.Api>("api").WithReference(sql);

builder.Build().Run();
```

```bash
aspire publish --publisher docker-compose --output-path ./compose
cd compose
docker compose up -d
```

Generated `docker-compose.yml`:
- One service per resource (containers + .NET projects as SDK-built images).
- Shared network for service discovery.
- `depends_on` derived from `WaitFor`.
- `secrets:` for parameters with `secret: true`.

**Team rule:** no `WithDataVolume()` or `ContainerLifetime.Persistent` anywhere — see [test-seeding.md](test-seeding.md). The Compose output therefore carries no `volumes:` for data. For environments that need persistence (a real production DB), provision a managed cloud resource and wire it via `AddConnectionString`, not local volumes.

## Kubernetes publisher

```bash
aspire publish --publisher kubernetes --output-path ./k8s
kubectl apply -f ./k8s
```

Output:
- `deployment-<resource>.yaml` per resource.
- `service-<resource>.yaml` for internal exposure.
- `ingress.yaml` for `WithExternalHttpEndpoints()` endpoints.
- `configmap-<resource>.yaml` for non-secret env vars.
- `secret-<resource>.yaml` for secret parameters.

## Hooks on publish

To mutate the model before the manifest is emitted:

```csharp
builder.Eventing.Subscribe<BeforePublishEvent>((e, ct) =>
{
    foreach (var resource in builder.Resources.OfType<ProjectResource>())
    {
        // e.g., add a manifest-publishing annotation
    }
    return Task.CompletedTask;
});
```

Or via a manual annotation:

```csharp
rb.WithAnnotation(new ManifestPublishingCallbackAnnotation(ctx =>
{
    ctx.Writer.WriteString("type", "my-custom-type.v0");
    ctx.Writer.WriteString("customField", "value");
    return Task.CompletedTask;
}));
```

## Gotchas

| Gotcha | Detail |
|---|---|
| Secrets as positional CLI args break Bicep | Use `WithEnvironment("KEY", param)` instead of `WithArgs(..., param)`. |
| `azd` deletes `obj/aspire-manifest-*` | Keep `PublishAsDockerfile` paths outside `obj/`. |
| Portal-edited env vars are overwritten | Source of truth is the AppHost; portal changes vanish on next `azd deploy`. |
| Dev dynamic ports do not carry to publish | Each resource gets deterministic ports from the target. Don't hardcode ports in client code. |
| `WithDataVolume()` / `Persistent` lifetime | Forbidden by team rule (see [test-seeding.md](test-seeding.md)). Production persistence comes from managed resources, not container volume annotations. |
| The dashboard is dev-only | Not published. In production, point OTel exporters at App Insights / Grafana / Datadog directly (the consumer's `AddServiceDefaults()` does this when `OTEL_EXPORTER_OTLP_ENDPOINT` points outside the dashboard). |

## Recommended pipeline

- CI runs `aspire publish --publisher azd`, commits `./infra` to a GitOps repo.
- Azure Pipelines / GitHub Actions runs `azd deploy` from the GitOps repo. Aspire stays out of the deploy path.
- Separate environments via `azd env` — never mix staging and production state.
- Parameters with `secret: true` for everything sensitive (consistency over case-by-case judgement).
- `PublishAsAzureContainerApp` for any Bicep customisation; never edit generated Bicep by hand.
- `builder.ExecutionContext.IsPublishMode` to condition the graph between dev and publish — minimally.

## Cross-references

- Live (deployment overview): https://learn.microsoft.com/en-us/dotnet/aspire/deployment/overview
- Live (Azure Container Apps via azd): https://learn.microsoft.com/en-us/dotnet/aspire/deployment/azure/aca-deployment
- Live (Docker Compose publisher): https://learn.microsoft.com/en-us/dotnet/aspire/deployment/docker-compose
- Live (Kubernetes publisher): https://learn.microsoft.com/en-us/dotnet/aspire/deployment/kubernetes
- Live (manifest format): https://learn.microsoft.com/en-us/dotnet/aspire/deployment/manifest-format
- Sibling: [scaffolding.md](scaffolding.md) (greenfield + Compose enrollment), [emulators-and-real-infra.md](emulators-and-real-infra.md) (single-flag dev/prod switching), [troubleshooting.md](troubleshooting.md) E section.
