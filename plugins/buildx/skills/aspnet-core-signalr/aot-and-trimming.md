# Trimming and Native AOT

.NET 10 supports trimming and Native AOT for SignalR. Constraints listed here.

## Supported scenarios

Trimming and Native AOT are supported for SignalR servers and clients on .NET 10.

## Constraints

- MessagePack-CSharp uses code generation; in AOT environments register pre-generated resolvers (`StaticCompositeResolver`).
- Hub-method DI without `[FromServices]` requires a DI container that implements `IServiceProviderIsService`.
- Strongly-typed hub interfaces (`Hub<T>`, `IHubContext<THub, T>`) are AOT-friendly.
