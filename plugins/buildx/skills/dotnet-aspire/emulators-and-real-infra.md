# Emulators / Stubs ↔ Real Infrastructure

Goal: the same AppHost (and the same consumer code) runs against local emulators / stubs **or** real cloud infrastructure, switched by a single binary flag. The consumers never know which side is active.

## 1. Three resource categories

Mental separation upfront — each category has different mechanics.

| Category | Examples | Local mode | Real mode |
|---|---|---|---|
| **A — Azure with native Aspire integration** | Cosmos DB, Service Bus, Storage, SQL, Redis, Key Vault, Event Hubs, PostgreSQL flexible | `RunAsEmulator()` on the Aspire builder | `AsExisting(...)` or `AddConnectionString(...)` |
| **B — Container-stub for a backend without native Aspire integration** | AWS S3/SQS/DynamoDB (LocalStack), MinIO for S3, Vault, RabbitMQ, MongoDB stubs | `AddContainer(...)` for the stub + composed connection string | `AddConnectionString(...)` from configuration / secrets |
| **C — Third-party HTTP service without an emulator** | Partner SOAP/REST APIs, internal legacy services | `AddContainer("...", "wiremock/...")` or a project-stub | `AddExternalService(name, urlParam)` |

The objective: the consumer reads the **same name** (`cosmos`, `aws`, `partner-api`) regardless of mode. Aspire injects the right connection string / endpoint under the hood.

## 2. The single flag

Read it once at the top of `Program.cs`:

```csharp
var builder = DistributedApplication.CreateBuilder(args);
var useReal = builder.Configuration.GetValue<bool>("UseRealInfrastructure");
```

The `args` array passed to `DistributedApplicationTestingBuilder.CreateAsync<T>(args)` (or to `aspire run` / `dotnet run --` on the command line) populates `builder.Configuration` automatically, so a fixture can flip the flag like this:

```csharp
var appHost = await DistributedApplicationTestingBuilder
    .CreateAsync<Projects.Contoso_Foo_AppHost>("UseRealInfrastructure=true");
```

**Resist per-resource flags.** `UseRealCosmos`, `UseRealAws`, `UseRealPartner` independently produce a combinatorial matrix that nobody runs in production. If you genuinely need mixed states, use a discrete `IntegrationTestProfile` enum (`AllEmulated`, `AllReal`, `RealAzureStubExternal`) — small set of named, meaningful profiles, not orthogonal flags.

## 3. Category A — Azure with native Aspire

### Pattern

```csharp
var cosmos = builder.AddAzureCosmosDB("cosmos");
var sb     = builder.AddAzureServiceBus("sb");
var stor   = builder.AddAzureStorage("storage");

if (!useReal)
{
    cosmos.RunAsEmulator();   // before AddDatabase / AddContainer
    sb.RunAsEmulator();
    stor.RunAsEmulator();
}

var db    = cosmos.AddDatabase("appdb");
var queue = sb.AddQueue("orders");
var blobs = stor.AddBlobs("blobs");

builder.AddProject<Projects.Api>("api")
    .WithReference(db).WaitFor(db)
    .WithReference(queue).WaitFor(queue)
    .WithReference(blobs).WaitFor(blobs);
```

### Real mode with managed identity (preferred)

When real, the connection strings come from configuration. Use identity-based formats so there are no secrets to rotate:

```
# Cosmos
ConnectionStrings:cosmos = "AccountEndpoint=https://my-cosmos.documents.azure.com:443/;"

# Service Bus
ConnectionStrings:sb     = "Endpoint=sb://my-sb.servicebus.windows.net/;Authentication=Managed Identity"

# Storage
ConnectionStrings:storage = "BlobEndpoint=https://my-storage.blob.core.windows.net/;"
```

The Azure SDKs see the missing keys, fall back to `DefaultAzureCredential`, and authenticate via OIDC / federated credentials.

### Why `RunAsEmulator()` order matters

`RunAsEmulator()` is defined on the **parent** Azure builder (`IResourceBuilder<AzureCosmosDBResource>`). Once you call `.AddDatabase(...)` the type changes (returns the database resource), and `RunAsEmulator()` no longer exists in scope. Call it **directly after `AddAzureXxx`, before any child resource is added**.

### `AsExisting` vs `AddConnectionString` (real mode)

- `AsExisting(parameter)` — Aspire treats the resource as **already provisioned**, references the existing Azure object via parameter (resource ID / FQDN), and skips provisioning. Use when the team has out-of-band provisioning (Bicep, Terraform).
- `AddConnectionString(name)` — Aspire reads the connection string from configuration verbatim, no resource modeling. Simplest in most test contexts.

```csharp
if (useReal)
{
    var cosmosCs = builder.AddConnectionString("cosmos");
    builder.AddProject<Projects.Api>("api").WithReference(cosmosCs);
}
```

## 4. Category B — Container stub for a backend without native integration

### Pattern (LocalStack for AWS)

```csharp
IResourceBuilder<IResourceWithConnectionString> aws;

if (useReal)
{
    aws = builder.AddConnectionString("aws");
}
else
{
    var localstack = builder.AddContainer("localstack", "localstack/localstack")
        .WithEnvironment("SERVICES", "s3,sqs,dynamodb")
        .WithEnvironment("DEFAULT_REGION", "us-east-1")
        .WithHttpEndpoint(targetPort: 4566, name: "edge")
        .WithLifetime(ContainerLifetime.Persistent);

    aws = builder.AddConnectionString(
        "aws",
        ReferenceExpression.Create(
            $"ServiceUrl={localstack.GetEndpoint("edge")};" +
            $"AccessKey=test;SecretKey=test;Region=us-east-1"));
}

builder.AddProject<Projects.Api>("api").WithReference(aws);
```

The consumer parses the custom connection string itself — `AddConnectionString` only carries a string, so the format is your contract:

```csharp
var awsCs = builder.Configuration.GetConnectionString("aws")!;
var awsConfig = ParseAwsConnectionString(awsCs);
builder.Services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client(
    new BasicAWSCredentials(awsConfig.AccessKey, awsConfig.SecretKey),
    new AmazonS3Config
    {
        ServiceURL = awsConfig.ServiceUrl,
        ForcePathStyle = true,           // required for LocalStack
        AuthenticationRegion = awsConfig.Region,
    }));
```

### Why `ReferenceExpression` for the connection string

`localstack.GetEndpoint("edge")` returns an `EndpointReference` that is **lazy** — it has no value until the container has bound a port. Concatenating it with normal string interpolation captures an unresolved object. `ReferenceExpression.Create($"...")` builds a deferred string that resolves at consumer-startup time.

### Real-mode AWS connection strings

```
# With OIDC (no static keys — preferred)
ServiceUrl=https://s3.us-east-1.amazonaws.com;Region=us-east-1

# With explicit keys
ServiceUrl=https://s3.us-east-1.amazonaws.com;AccessKey=AKIA...;SecretKey=...;Region=us-east-1
```

Omit `AccessKey`/`SecretKey` and the SDK falls back to its credential chain (IMDS, OIDC, env vars).

## 5. Category C — Third-party HTTP service

### Pattern with WireMock as stub

```csharp
IResourceBuilder<IResourceWithEndpoints> partner;

if (useReal)
{
    var partnerUrl = builder.AddParameter("partnerApiUrl");
    partner = builder.AddExternalService("partner-api", partnerUrl)
        .WithHttpHealthCheck("/health");
}
else
{
    partner = builder.AddContainer("partner-api", "wiremock/wiremock", "3.9.1")
        .WithBindMount("./stubs/partner-api", "/home/wiremock", isReadOnly: true)
        .WithHttpEndpoint(targetPort: 8080, name: "http")
        .WithLifetime(ContainerLifetime.Session);
}

builder.AddProject<Projects.Api>("api")
    .WithReference(partner)        // services__partner-api__http__0
    .WaitFor(partner);
```

### `AddExternalService` vs `AddConnectionString` for HTTP

`AddExternalService` participates in **service discovery** — the consumer uses `HttpClient` with `BaseAddress = new Uri("https://partner-api")` and Aspire resolves the URL. `AddConnectionString` carries an opaque string that the consumer must parse. Prefer `AddExternalService` for HTTP / gRPC.

### Caveat: `WaitFor` on `AddExternalService`

In current Aspire, `WaitFor(externalService)` does not actually block startup, even if the external service has a health check. The Aspire runtime observes the health but lets the consumer start anyway. Build resilience into the consumer:

```csharp
builder.Services.AddHttpClient<PartnerApiClient>(c =>
        c.BaseAddress = new Uri("https://partner-api"))
    .AddStandardResilienceHandler();
```

For Category C **local** (where you declared a real container as the stub), `WaitFor` works normally — the container is a first-class Aspire resource.

### WireMock stubs on disk

Mappings under `./stubs/partner-api/mappings/*.json`:

```json
{
  "request":  { "method": "GET", "url": "/customers/123" },
  "response": {
    "status":   200,
    "headers":  { "Content-Type": "application/json" },
    "jsonBody": { "id": 123, "name": "Acme Corp", "tier": "gold" }
  }
}
```

Matchers: method, path (regex), headers, query params, body (JSONPath / XPath). Responses can include delays, intermittent failures, Handlebars templates, conditional forwarding.

WireMock has no built-in `/health`. Two options:
- `WithHttpHealthCheck("/__admin/mappings")` — built-in admin endpoint, returns 200 when the server is up.
- Define a mapping for `/health` returning 200.

### When WireMock vs a `.NET` stub project

- **WireMock**: contract is stable and voluminous (especially with OpenAPI), needs record-and-replay, simulates failures (latency, intermittent 5xx, dropped connections) declaratively, edited by QA in JSON.
- **.NET stub project**: stub needs business logic or state across requests (POST/GET coherence), shared DTOs with the system under test, debugger breakpoints.

```csharp
// .NET stub variant
partner = builder.AddProject<Projects.PartnerApi_Stub>("partner-api");
```

### Alternatives to WireMock

- **Microcks** — consumes OpenAPI/AsyncAPI/SOAP specs directly, generates the stub. Less custom flexibility but cheap to keep in sync with the spec.
- **Mountebank**, **Prism**, **Hoverfly** — different trade-offs, same `AddContainer` pattern.

## 6. Composing the AppHost

When the resource list grows past ~8, extract per-category extensions:

```csharp
public static class AppHostExtensions
{
    public static IResourceBuilder<IResourceWithConnectionString> AddAws(
        this IDistributedApplicationBuilder builder, bool useReal)
    {
        if (useReal) return builder.AddConnectionString("aws");

        var ls = builder.AddContainer("localstack", "localstack/localstack")
            .WithEnvironment("SERVICES", "s3,sqs,dynamodb")
            .WithHttpEndpoint(targetPort: 4566, name: "edge")
            .WithLifetime(ContainerLifetime.Persistent);

        return builder.AddConnectionString(
            "aws",
            ReferenceExpression.Create(
                $"ServiceUrl={ls.GetEndpoint("edge")};" +
                $"AccessKey=test;SecretKey=test;Region=us-east-1"));
    }
}
```

Keeps `Program.cs` legible:

```csharp
var aws     = builder.AddAws(useReal);
var partner = builder.AddPartnerApi(useReal);
```

## 7. Real-mode configuration & secrets

### Local development
Default to emulators only. Real-mode env vars are not set on developer machines.

### CI — emulated job
Any runner with Docker; no secrets. The default test profile.

### CI — real-infra job
Separate job, federated identity (OIDC / workload identity), no static credentials. Secrets sourced from Key Vault or pipeline variables, exposed as env vars to `dotnet test`. Filter: `--filter "TestCategory=RealInfra"` (see [integration-testing.md](integration-testing.md) § 6).

## 8. Operational guidance

- **Health checks on every stub.** A WireMock container without a working health check fails `WaitFor`, hangs the AppHost, then times out. Either the admin endpoint or a hand-rolled `/health` mapping.
- **Persistent volumes for stateful stubs.** Postgres, Redis, MinIO benefit from `WithLifetime(ContainerLifetime.Persistent)` + `WithDataVolume(...)` so iterating between `aspire run` invocations doesn't lose state.
- **`WaitForResourceHealthyAsync` timeout 3 minutes.** Cold-start of Service Bus / Cosmos in real mode regularly crosses 30 seconds; the default is too aggressive. Test fixtures pass `TimeSpan.FromMinutes(3)`.
- **Don't mix categories in the consumer SDK.** If the API uses Cosmos via `Microsoft.Azure.Cosmos`, keep using it in real mode — there is **no parallel emulator-only client SDK** that the consumer should switch to. The flag is in the AppHost only.

## Cross-references

- Live (`AddExternalService`): https://learn.microsoft.com/en-us/dotnet/api/aspire.hosting.externalservicebuilderextensions.addexternalservice
- Live (Cosmos DB integration with `RunAsEmulator` / `AsExisting`): https://learn.microsoft.com/en-us/dotnet/aspire/database/azure-cosmos-db-integration
- Live (Local Azure provisioning): https://learn.microsoft.com/en-us/dotnet/aspire/azure/local-provisioning
- Live (Service Bus emulator): https://learn.microsoft.com/en-us/dotnet/aspire/messaging/azure-service-bus-integration
- Live (`AddConnectionString`): https://learn.microsoft.com/en-us/dotnet/api/aspire.hosting.parameterresourcebuilderextensions.addconnectionstring
- Live (LocalStack): https://docs.localstack.cloud/overview/
- Live (WireMock): https://wiremock.org/docs/
- Live (Microcks): https://microcks.io/
- Sibling: [integration-testing.md](integration-testing.md) — passing `UseRealInfrastructure=true` from a fixture.
