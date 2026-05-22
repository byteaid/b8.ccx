# Auth, Health Checks, and Reflection

JWT, mTLS, `[Authorize]`, `CallCredentials`, `AddCallCredentials`, `AddGrpcHealthChecks`, `AddGrpcReflection`.

## Authentication & authorization

Pipeline order:

```csharp
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapGrpcService<GreeterService>();
```

Server-side user: `ClaimsPrincipal user = ctx.GetHttpContext().User;`.

### JWT bearer per call

```csharp
var headers = new Metadata { { "Authorization", $"Bearer {token}" } };
var resp = await client.SayHelloAsync(req, headers);
```

### Centralized via `CallCredentials` (TLS only)

```csharp
var creds = CallCredentials.FromInterceptor(async (ctx, metadata) =>
{
    var t = await tokenProvider.GetTokenAsync(ctx.CancellationToken);
    metadata.Add("Authorization", $"Bearer {t}");
});
var channel = GrpcChannel.ForAddress(addr, new GrpcChannelOptions
{
    Credentials = ChannelCredentials.Create(new SslCredentials(), creds)
});
```

### Client-factory `AddCallCredentials`

```csharp
builder.Services
    .AddGrpcClient<Greeter.GreeterClient>(o => o.Address = new Uri(...))
    .AddCallCredentials(async (ctx, metadata, sp) =>
    {
        var t = await sp.GetRequiredService<ITokenProvider>().GetTokenAsync(ctx.CancellationToken);
        metadata.Add("Authorization", $"Bearer {t}");
    });
```

### mTLS

```csharp
var h = new HttpClientHandler();
h.ClientCertificates.Add(cert);
var ch = GrpcChannel.ForAddress(addr, new GrpcChannelOptions { HttpHandler = h });
```

Server: configure Kestrel to require client certs and use the `certauth` package to map to `ClaimsPrincipal`.

### `[Authorize]` on services / methods

```csharp
[Authorize]
public class TicketerService : Ticketer.TicketerBase
{
    public override Task<...> GetAvailableTickets(Empty r, ServerCallContext c) { /* ... */ }

    [Authorize("Administrators")]
    public override Task<...> RefundTickets(BuyTicketsRequest r, ServerCallContext c) { /* ... */ }
}

app.MapGrpcService<TicketerService>().RequireAuthorization("Administrators");
app.MapGrpcService<PublicService>().AllowAnonymous();
```

Supported flows: Microsoft Entra ID, Client Cert, IdentityServer, JWT bearer, OAuth 2.0, OIDC, WS-Fed. **NOT** Windows auth (NTLM/Kerberos/Negotiate). For depth on flows / token issuance / Identity, load `aspnet-core-security`.

## Health checks

Implements `grpc.health.v1.Health` (`Check`, `Watch`):

```csharp
builder.Services.AddGrpc();
builder.Services.AddGrpcHealthChecks()
                .AddCheck("Sample", () => HealthCheckResult.Healthy());

app.MapGrpcService<GreeterService>();
app.MapGrpcHealthChecksService();
```

Mapping:

| .NET status | gRPC `ServingStatus` |
|---|---|
| no results yet | `Unknown` |
| any `Unhealthy` | `NotServing` |
| otherwise | `Serving` |
| unknown service name (`Check`) | `NOT_FOUND` |
| unknown service name (`Watch`) | `Unknown` |

Selective groups:

```csharp
builder.Services.AddGrpcHealthChecks(o =>
{
    o.Services.MapService("",              r => r.Tags.Contains("public"));
    o.Services.MapService("greet.Greeter", r => r.Tags.Contains("greeter"));
});
```

Health endpoint inherits app-wide auth — `app.MapGrpcHealthChecksService().AllowAnonymous();` to expose anonymously. Used by Kubernetes gRPC probes.

## Reflection (server)

```csharp
builder.Services.AddGrpcReflection();
if (app.Environment.IsDevelopment()) app.MapGrpcReflectionService();
```

Lets `grpcurl` / Postman discover services without `.proto` files. **Gate to development.**
