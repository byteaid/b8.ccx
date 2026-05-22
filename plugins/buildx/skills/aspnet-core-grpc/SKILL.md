---
name: aspnet-core-grpc
description: ASP.NET Core gRPC reference for .NET 10. Covers `Grpc.AspNetCore` server (`AddGrpc`, `MapGrpcService<T>`, Kestrel HTTP/2 + TLS), `.proto` + `<Protobuf>` codegen, the four method shapes (unary / server-stream / client-stream / bidi), `RpcException` + `Status`, protobuf types (scalars, wrappers, well-knowns, oneof, Any, ByteString), `Grpc.Net.Client` channels, `Grpc.Net.ClientFactory` + `EnableCallContextPropagation`, deadlines/cancellation, retries/hedging via `ServiceConfig`, interceptors, JWT/mTLS auth, gRPC-Web (`GrpcWebHandler`), JSON transcoding, health checks, reflection, code-first via `protobuf-net.Grpc`, AOT/trimming, channel reuse + HTTP/2 multiplex tuning.
when_to_use: |
  - Trigger keywords: gRPC, Grpc.AspNetCore, Grpc.Net.Client, AddGrpc, MapGrpcService, .proto, Protobuf item, GrpcChannel, ServerCallContext, IServerStreamWriter, IAsyncStreamReader, RpcException, StatusCode, ServiceConfig, RetryPolicy, HedgingPolicy, EnableCallContextPropagation, AddCallCredentials, GrpcWebHandler, JSON transcoding, AddGrpcHealthChecks, AddGrpcReflection, protobuf-net.Grpc, ByteString, oneof, MaxStreamsPerConnection.
  - Task shapes: scaffold a gRPC server; pick HTTP/2 endpoint + TLS config; author a `.proto` and wire `<Protobuf>`; implement each of the four method shapes; add retries/hedging; configure `MaxReceiveMessageSize`; secure with JWT/mTLS; expose gRPC-Web for browsers; add JSON transcoding; add health checks + reflection; debug `UNAVAILABLE`/`DeadlineExceeded`; tune HTTP/2 stream concurrency.
allowed-tools: Bash, Edit, Glob, Grep, Monitor, NotebookEdit, PowerShell, Read, Write
user-invocable: false
paths: ["**/*.cs", "**/*.proto", "**/*.csproj", "**/Program.cs", "**/appsettings*.json"]
---

# ASP.NET Core gRPC — Reference

Reference for authoring and reviewing gRPC services and clients on .NET 10. Pin the rules; load the matching sub-file for depth.

## Mental model

- gRPC = contract-first RPC over **HTTP/2** with **Protobuf** binary payloads. Mandatory transport: HTTP/2; mandatory wire: Protobuf (gRPC-Web and JSON transcoding bridge to HTTP/1.1 / JSON for browser scenarios).
- The `.proto` file is the source of truth. `Grpc.Tools` runs the `protoc` plugin at build to emit `Foo.cs` (in `obj/`, never source-controlled) — base server class + client stub + message types.
- Every RPC fits one of four shapes: **unary**, **server streaming**, **client streaming**, **bidirectional**. The shape is declared in the `.proto` (`stream` keyword on request, response, both, or neither) and dictates the C# signature on both sides.
- Errors travel as `Status(StatusCode, Detail)` carried in trailers; the client surfaces them as `RpcException`.
- A `GrpcChannel` is a long-lived multiplexed HTTP/2 connection — **reuse it for the lifetime of the app**. Per-call `Greeter.GreeterClient` instances are cheap.

## Non-negotiable rules

1. **HTTP/2 endpoint or it's not gRPC.** `Protocols = HttpProtocols.Http2` for cleartext, or `Http1AndHttp2` **only** with TLS (ALPN selects). No TLS + `Http1AndHttp2` -> connection falls back to HTTP/1.1 and gRPC fails.
2. **Always `await` the `*Async` client method.** Generated unary clients have both `SayHello` (blocking) and `SayHelloAsync`. Blocking from async code -> thread-pool starvation, deadlocks, **Blazor WASM hang**.
3. **Reuse `GrpcChannel`.** Channel construction = TCP + TLS + HTTP/2 handshake. New per-request -> catastrophic latency.
4. **Dispose every streaming call** (`using var call = ...`). Otherwise leaks server resources, holds an HTTP/2 stream.
5. **`string` / `ByteString` cannot be `null`** (proto3 default = empty). Use the wrapper types from `google/protobuf/wrappers.proto` for true nullability.
6. **`CallCredentials` are no-op on plaintext channels** unless `UnsafeUseInsecureChannelCallCredentials = true`. **Never** flip that in production.
7. **Forward `ServerCallContext.CancellationToken` to every downstream call** in the server method. The deadline + client-cancel both fire it.
8. **Don't catch a non-`RpcException` server-side and rethrow as `Exception`** — the client gets `UNKNOWN`. Use `throw new RpcException(new Status(StatusCode.X, "..."), trailers);`.
9. **`EnableDetailedErrors = true` is dev only** — leaks server detail to every caller.
10. **`.proto` files live in a dedicated contracts project** — see `dotnet-conventions` § forbidden-patterns/no-proto-outside-dedicated-project.
11. **Windows auth (NTLM/Kerberos/Negotiate) is not supported.** HTTP/2 forbids it. Use JWT/OIDC/mTLS.
12. **`IServerStreamWriter<T>.WriteAsync` and `IAsyncStreamReader<T>.MoveNext` are single-threaded each.** Reader on one thread + writer on another is fine; concurrent writes from two threads is not. Use `System.Threading.Channels.Channel<T>` to fan-in.

## Sub-index

| Topic | File | Load when |
|---|---|---|
| Server bootstrap, Kestrel HTTP/2 + TLS, four method shapes, errors, channels, client factory, deadlines, retries, hedging, interceptors, server config, performance, diagnostics | [server-and-client.md](server-and-client.md) | Authoring or reviewing the server, .NET client, retry/hedging, performance, or logging/metrics surface. |
| `.proto` syntax, `<Protobuf>` MSBuild item, scalar/wrapper/well-known types, decimal DIY, collections, oneof, Any, ByteString, code-first via `protobuf-net.Grpc` | [proto-and-codegen.md](proto-and-codegen.md) | Designing or modifying `.proto`, hitting type-mapping questions, or comparing proto-first vs code-first. |
| JWT, mTLS, `[Authorize]`, `CallCredentials`, `AddCallCredentials`, `AddGrpcHealthChecks`, `AddGrpcReflection` | [auth-health-reflection.md](auth-health-reflection.md) | Securing a gRPC service, wiring health probes (Kubernetes), or exposing reflection in dev. |
| gRPC-Web (`GrpcWebHandler`, CORS), JSON transcoding (`AddJsonTranscoding`, `google.api.http`) | [grpc-web-and-transcoding.md](grpc-web-and-transcoding.md) | Bridging gRPC to browsers or REST/JSON callers. |
| gRPC vs HTTP API matrix, common pitfalls catalogue | [decision-and-pitfalls.md](decision-and-pitfalls.md) | Deciding gRPC vs REST, or debugging an unexpected `UNKNOWN`/`UNAVAILABLE`. |

## Quick decision matrix

| Question | Answer |
|---|---|
| Microservice point-to-point, low latency | gRPC. Load `server-and-client.md`. |
| Browser client | gRPC-Web or JSON transcoding. Load `grpc-web-and-transcoding.md`. |
| Broadcast / fan-out to many clients | NOT gRPC — use `aspnet-core-signalr`. |
| Auth flow depth (JWT issuance, OIDC, Identity) | `aspnet-core-security`. This skill only documents the gRPC-specific surface. |
| Nullable scalar field | `google/protobuf/wrappers.proto`. Load `proto-and-codegen.md`. |
| Need retry on `UNAVAILABLE` | `ServiceConfig` + `RetryPolicy`. Load `server-and-client.md`. |
| Need parallel attempts (idempotent) | `HedgingPolicy`. Load `server-and-client.md`. |

## Cross-references

- Public docs (Overview): https://learn.microsoft.com/en-us/aspnet/core/grpc/?view=aspnetcore-10.0
- Public docs (Server / Kestrel): https://learn.microsoft.com/en-us/aspnet/core/grpc/aspnetcore?view=aspnetcore-10.0
- Public docs (.NET client): https://learn.microsoft.com/en-us/aspnet/core/grpc/client?view=aspnetcore-10.0
- Public docs (Client factory): https://learn.microsoft.com/en-us/aspnet/core/grpc/clientfactory?view=aspnetcore-10.0
- Public docs (Protobuf types): https://learn.microsoft.com/en-us/aspnet/core/grpc/protobuf?view=aspnetcore-10.0
- Public docs (Deadlines / cancellation): https://learn.microsoft.com/en-us/aspnet/core/grpc/deadlines-cancellation?view=aspnetcore-10.0
- Public docs (Retries / hedging): https://learn.microsoft.com/en-us/aspnet/core/grpc/retries?view=aspnetcore-10.0
- Public docs (Interceptors): https://learn.microsoft.com/en-us/aspnet/core/grpc/interceptors?view=aspnetcore-10.0
- Public docs (Auth): https://learn.microsoft.com/en-us/aspnet/core/grpc/authn-and-authz?view=aspnetcore-10.0
- Public docs (Diagnostics): https://learn.microsoft.com/en-us/aspnet/core/grpc/diagnostics?view=aspnetcore-10.0
- Public docs (Performance): https://learn.microsoft.com/en-us/aspnet/core/grpc/performance?view=aspnetcore-10.0
- Public docs (gRPC-Web): https://learn.microsoft.com/en-us/aspnet/core/grpc/grpcweb?view=aspnetcore-10.0
- Public docs (JSON transcoding): https://learn.microsoft.com/en-us/aspnet/core/grpc/json-transcoding?view=aspnetcore-10.0
- Public docs (Comparison): https://learn.microsoft.com/en-us/aspnet/core/grpc/comparison?view=aspnetcore-10.0
- Related skill: `dotnet-conventions` § forbidden-patterns/no-proto-outside-dedicated-project — `.proto` files belong in a dedicated contracts project.
- Related skill: `aspnet-core-signalr` — for broadcast / pub-sub real-time (gRPC has no broadcast primitive).
- Related skill: `aspnet-core-yarp` — when YARP fronts gRPC traffic.
- Related skill: `aspnet-core-security` — for JWT issuance, OIDC, Identity, mTLS auth wiring depth.
- Related skill: `dotnet-asynchronous-programming` — `async`/`await`, `CancellationToken` semantics that gRPC clients and servers rely on.
