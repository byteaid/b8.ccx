# gRPC-Web and JSON Transcoding

Browser bridges. gRPC-Web for binary-over-HTTP/1.1; JSON transcoding for REST/JSON over the same gRPC service.

## gRPC-Web

For browser clients (no native HTTP/2 control) and HTTP/1.1 environments.

### Server

```csharp
builder.Services.AddGrpc();
var app = builder.Build();
app.UseGrpcWeb();                                  // after routing, before endpoints
app.MapGrpcService<GreeterService>().EnableGrpcWeb();
// Or: app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });
```

CORS for browser callers — must expose gRPC-specific headers:

```csharp
builder.Services.AddCors(o => o.AddPolicy("AllowAll", b =>
    b.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()
     .WithExposedHeaders("Grpc-Status", "Grpc-Message",
        "Grpc-Encoding", "Grpc-Accept-Encoding", "Grpc-Status-Details-Bin")));

app.UseGrpcWeb();
app.UseCors();
app.MapGrpcService<GreeterService>().EnableGrpcWeb().RequireCors("AllowAll");
```

### Streaming limits

- Browser gRPC-Web: NO client-streaming, NO bidi.
- .NET gRPC-Web client over HTTP/1.1: NO client-streaming, NO bidi.
- Azure App Service / IIS: NO bidi.
- Server streaming OK in browser.

### .NET client -> gRPC-Web

```csharp
var channel = GrpcChannel.ForAddress("https://localhost:53305", new GrpcChannelOptions
{
    HttpHandler = new GrpcWebHandler(new HttpClientHandler())
});
```

`GrpcWebHandler.GrpcWebMode`: `GrpcWeb` (default, `application/grpc-web`) / `GrpcWebText` (base64; required for server-streaming in browsers).

Mixed protocol endpoint requires TLS:

```json
{ "Kestrel": { "EndpointDefaults": { "Protocols": "Http1AndHttp2" } } }
```

## JSON transcoding

Browser-friendly REST/JSON over the same gRPC service. Production-supported on .NET 10.

```xml
<PackageReference Include="Microsoft.AspNetCore.Grpc.JsonTranscoding" Version="..." />
<PropertyGroup><IncludeHttpRuleProtos>true</IncludeHttpRuleProtos></PropertyGroup>
```

```csharp
builder.Services.AddGrpc().AddJsonTranscoding();
```

```protobuf
import "google/api/annotations.proto";
service Greeter {
  rpc SayHello (HelloRequest) returns (HelloReply) {
    option (google.api.http) = { get: "/v1/greeter/{name}" };
  }
}
```

`GET /v1/greeter/world` -> `{"message": "Hello world"}`. The same RPC remains callable as HTTP/2 gRPC. **Only server streaming** is supported (line-delimited JSON); client streaming and bidi are not.

| | Transcoding | gRPC-Web | grpc-gateway |
|---|---|---|---|
| Hosting | in-proc | in-proc | external proxy |
| Browser needs gRPC client | NO | YES | NO |
| Wire | JSON | Protobuf (binary or base64) | JSON |
| Latency cost | none | none | +1 hop |
