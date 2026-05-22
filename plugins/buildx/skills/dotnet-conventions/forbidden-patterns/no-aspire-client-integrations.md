# Forbidden — Aspire client integration packages

## What it looks like

```xml
<!-- Acme.Foo.WebAPI.csproj — banned references in a host project -->
<PackageReference Include="Aspire.Microsoft.EntityFrameworkCore.SqlServer" Version="..." />
<PackageReference Include="Aspire.Microsoft.SqlClient" Version="..." />
<PackageReference Include="Aspire.StackExchange.Redis" Version="..." />
<PackageReference Include="Aspire.Microsoft.Azure.Cosmos" Version="..." />
<PackageReference Include="Aspire.RabbitMQ.Client" Version="..." />
```

```csharp
// Program.cs
builder.AddSqlServerDbContext<AppDb>("gymtrackerdb");           // banned
builder.AddRedisClient("cache");                                 // banned
builder.AddRabbitMQClient("rabbit");                             // banned
```

## Why it's banned

1. **Standard clients are more portable.** `services.AddDbContext<AppDb>(opts => opts.UseSqlServer(cs))` is universally understood by every .NET developer; `builder.AddSqlServerDbContext<AppDb>("name")` is Aspire-specific.
2. **Tighter control over connection-string handling.** Containerized SQL Server requires `TrustServerCertificate=true` in the connection string; the team controls this explicitly via `SqlConnectionStringBuilder` in the host. The Aspire integration hides that customization point.
3. **Cleaner debugging.** When something goes wrong, the team reads the standard client's source — not a wrapper.
4. **Lower coupling.** Removing Aspire from a host (e.g., switching to plain Kubernetes) requires replacing the client integration; with standard clients, only the configuration source changes.
5. **Team decision.** Documented and consistent across every solution. Inconsistency hurts more than the convenience the wrapper offers.

## What to do instead

The contract is: **AppHost registers the resource and `WithReference(consumer)`. Aspire injects `ConnectionStrings__<name>` into the consumer's environment. The consumer reads it with `GetConnectionString`.**

```csharp
// Program.cs of the host
var rawCs = builder.Configuration.GetConnectionString("gymtrackerdb")
    ?? throw new InvalidOperationException("Connection string 'gymtrackerdb' missing.");

var csBuilder = new SqlConnectionStringBuilder(rawCs)
{
    TrustServerCertificate = true,    // mandatory for containerized SQL Server
    Encrypt = true,
};

builder.Services.AddDbContext<AppDb>(opts => opts.UseSqlServer(csBuilder.ConnectionString));
builder.Services.AddStackExchangeRedisCache(opts =>
    opts.Configuration = builder.Configuration.GetConnectionString("cache"));

builder.Services.AddHttpClient<IPaymentApiClient, PaymentApiClient>(c =>
    c.BaseAddress = new Uri("http://payments"));   // Aspire service discovery resolves "payments"
```

## Enforcement

- **Banned packages in any host project:** the `Aspire.<Vendor>.<Product>` family. The `Aspire.Hosting.<Vendor>.<Product>` family is fine — those are AppHost-side and used by the Aspire engineer.
- **On sight, inside a host's `.csproj` you're editing:** remove the client integration package, replace the registration with the standard client, and verify the connection-string customization (TLS, retry, etc.) is preserved.
- **Quick scan:**

  ```bash
  grep -rE "Aspire\.(Microsoft|StackExchange|RabbitMQ|MongoDB|MySql|Pomelo|Npgsql)\." src/*.WebAPI/ src/*.Worker/ src/*.Web/ src/*.gRPC.Server/
  ```

  must return no matches.

## Exception

The architect may explicitly allow a specific client integration in `docs/ARCHITECTURE.md` for a documented reason (e.g., a specific OpenTelemetry hookup that the standard client doesn't surface). Without that record, the integration is banned.

## See also

- `dotnet-aspire` § apphost-wiring — how resources are registered on the AppHost side.
- [../csharp-style/dotnet-cli-only.md](../csharp-style/dotnet-cli-only.md)
