# Streaming, Hub Filters, MessagePack

Server-to-client and client-to-server streaming, `IHubFilter`, MessagePack hub protocol, hub-method polymorphism.

## Streaming

A hub method becomes a streaming method when its return type is `IAsyncEnumerable<T>`, `ChannelReader<T>`, `Task<IAsyncEnumerable<T>>`, or `Task<ChannelReader<T>>`. Becomes upload-streaming when it accepts `ChannelReader<T>` or `IAsyncEnumerable<T>` parameters.

### Server -> client

```csharp
public async IAsyncEnumerable<int> Counter(int count, int delay,
    [EnumeratorCancellation] CancellationToken cancellationToken)
{
    for (var i = 0; i < count; i++)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return i;
        await Task.Delay(delay, cancellationToken);
    }
}
```

`CancellationToken` fires when the **client unsubscribes**. `[EnumeratorCancellation]` propagates it from the call site.

`ChannelReader<T>` rules: write on a background task, **return the reader ASAP** (other invocations on the connection block until you do), always complete the writer in `finally`, flow exceptions via `writer.Complete(ex)`.

### Client -> server

```csharp
public async Task UploadStream(IAsyncEnumerable<string> stream)
{
    await foreach (var item in stream) Console.WriteLine(item);
}
```

### .NET client streaming

```csharp
// Server-to-client
var stream = hubConnection.StreamAsync<int>("Counter", 10, 500, cts.Token);
await foreach (var n in stream) Console.WriteLine(n);

// Or via ChannelReader
var ch = await hubConnection.StreamAsChannelAsync<int>("Counter", 10, 500, cts.Token);

// Client-to-server (auto-completes when iterator exits)
await connection.SendAsync("UploadStream", ClientStream());
```

### Streaming (JS)

```javascript
connection.stream("Counter", 10, 500).subscribe({
    next: item => {}, complete: () => {}, error: err => {}
});

// Upload
const subject = new signalR.Subject();
await connection.send("UploadStream", subject);
subject.next("data"); subject.complete();
```

## Hub filters (`IHubFilter`)

Middleware-style pipeline around hub method invocations and connection lifecycle. To **block** a call, throw `HubException` instead of calling `next`.

```csharp
public class CustomFilter : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext ctx, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        try { return await next(ctx); }
        catch (Exception ex) { throw; }
    }
    public Task OnConnectedAsync(HubLifetimeContext c, Func<HubLifetimeContext, Task> next)
        => next(c);
    public Task OnDisconnectedAsync(HubLifetimeContext c, Exception? ex,
        Func<HubLifetimeContext, Exception?, Task> next) => next(c, ex);
}

services.AddSignalR(o => o.AddFilter<CustomFilter>())
        .AddHubOptions<ChatHub>(o => o.AddFilter<CustomFilter2>());
```

Order = registration order; **global runs before per-hub**. Three registration shapes: `AddFilter<T>()` (DI/type-activated, **per-invocation lifetime**), `AddFilter(typeof(T))`, `AddFilter(new MyFilter())` (singleton instance, reused). For hot paths register the filter as a DI singleton or pass an instance. `[Authorize]` runs **before** hub filters.

## MessagePack hub protocol

```bash
dotnet add package Microsoft.AspNetCore.SignalR.Protocols.MessagePack
```

```csharp
services.AddSignalR().AddMessagePackProtocol(o =>
{
    o.SerializerOptions = MessagePackSerializerOptions.Standard
        .WithSecurity(MessagePackSecurity.UntrustedData);   // CVE-2020-5234
});
```

JSON stays enabled — both protocols are negotiated. Strict typing rules: no JSON-style `"42" <-> 42` coercion; no preservation of `DateTime.Kind` (always send UTC); JS payloads must use `PascalCase` to match C# property names (or `[Key]` to remap).

JS: `npm install @microsoft/signalr-protocol-msgpack`, then `.withHubProtocol(new signalR.protocols.msgpack.MessagePackHubProtocol())`.

AOT / MAUI / Unity: register `StaticCompositeResolver` with pre-generated resolvers (see `aot-and-trimming.md`).

## Hub method polymorphism (.NET 10)

`AddSignalR()` JSON handling supports `[JsonPolymorphic]` / `[JsonDerivedType]` on parameter and return types — discriminator-based deserialization works in hub methods.
