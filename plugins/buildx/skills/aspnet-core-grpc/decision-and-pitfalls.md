# gRPC vs HTTP API Decision Matrix and Pitfalls

When to use gRPC vs REST; the catalogue of common mistakes.

## gRPC vs HTTP API decision matrix

| Feature | gRPC | HTTP API + JSON |
|---|---|---|
| Contract | `.proto` mandatory | OpenAPI optional |
| Wire | HTTP/2 + Protobuf binary | HTTP/1.1+/2 + JSON text |
| Streaming | unary, server, client, bidi | client, server (not bidi) |
| Browser | NO native; gRPC-Web / transcoding bridges | YES |
| Codegen | first-class | OpenAPI tools |
| Human-readable wire | NO | YES |
| Auth flows | OAuth2/JWT/mTLS/OIDC; **NOT** Windows auth | all incl. Windows auth |

**Use gRPC when:** microservices with low-latency requirements, point-to-point real-time streaming, polyglot systems, network-constrained clients, IPC. **Avoid gRPC when:** browser-first APIs (use REST or gRPC-Web/transcoding), broadcast pub/sub real-time (use SignalR — gRPC has no broadcast primitive).

## Pitfalls cheatsheet

- `string` / `ByteString` cannot be assigned `null` — use wrapper types for nullability.
- Interceptor chain order: client interceptors execute in REVERSE of `.Intercept(...)` chain.
- `BlockingUnaryCall` and `AsyncUnaryCall` are NOT interchangeable — pick the right override.
- `CallCredentials` is no-op on plaintext channels unless `UnsafeUseInsecureChannelCallCredentials = true`.
- Retries skip if the call is already "committed" (response headers received OR send-buffer overflow).
- Streaming retries don't restart after the first response message has flowed.
- Disposing a streaming call that already completed gracefully is harmless.
- Don't increase Kestrel `MaxStreamsPerConnection` to fix queueing — fix it client-side via `EnableMultipleHttp2Connections`.
- IIS / Azure App Service: no bidi.
- Windows auth (NTLM/Kerberos/Negotiate): unsupported (HTTP/2).
