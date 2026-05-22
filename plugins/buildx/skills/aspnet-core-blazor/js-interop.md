# JavaScript Interop

`IJSRuntime`, module-isolation pattern, JS → .NET callbacks, `JSImport`/`JSExport`, `IJSObjectReference`. Load when authoring `.razor.js` modules, calling browser APIs, or marshalling structured data.

| Scenario | Transport | Sync available? |
|---|---|---|
| .NET → JS, Server | over SignalR | no — async only |
| .NET → JS, WASM | in-process | yes via `IJSInProcessRuntime` |
| JS → .NET, instance method | `DotNetObjectReference` | sync only on WASM |
| JS → .NET, static method | `[JSInvokable]` | sync only on WASM |

All cross-runtime calls are async by default so the same code runs on both Server and WASM.

## Module-isolation pattern (preferred)

`MyComp.razor.js` next to the component:

```javascript
export function showAlert(msg) { alert(msg); }
```

```razor
@implements IAsyncDisposable
@inject IJSRuntime JS

@code {
    private IJSObjectReference? module;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            module = await JS.InvokeAsync<IJSObjectReference>(
                "import", "./Components/Pages/MyComp.razor.js");
            await module.InvokeVoidAsync("showAlert", "loaded");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (module is not null)
        {
            try { await module.DisposeAsync(); }
            catch (JSDisconnectedException) { }   // server-side safety net
        }
    }
}
```

## JS → .NET

Static: `[JSInvokable] public static Task<string> ReturnGreetingAsync(string name) => ...;` then JS `await DotNet.invokeMethodAsync('MyAssembly', 'ReturnGreetingAsync', 'Ada');`.

Instance: pass `DotNetObjectReference.Create(helper)` to JS; dispose it in `IDisposable.Dispose`.

## `JSImport`/`JSExport` (WASM only, .NET 7+)

Source-generated, marshalling-light alternative to `IJSRuntime`. Lower overhead, type-safe, **no SignalR support** — WASM-only.

```csharp
using System.Runtime.InteropServices.JavaScript;

public partial class JSHost
{
    [JSImport("globalThis.console.log")]
    public static partial void Log(string message);

    [JSImport("greet", "myModule")]
    public static partial Task<string> GreetAsync(string name);

    [JSExport] public static int Add(int a, int b) => a + b;
}

await JSHost.ImportAsync("myModule", "./myModule.js");
```

Supported types: primitives, `string`, `Task` / `Task<T>`, `JSObject`, `byte[]`, `int[]`, `double[]`, `string[]`, function refs (`Action`/`Func` of supported types).

## `IJSObjectReference` (.NET 10)

Adds `InvokeConstructorAsync`, `GetValueAsync`, `SetValueAsync` — read/write JS properties and instantiate JS classes from C#.

## Other notes

- Default serializer is `System.Text.Json`. `JsonSerializerIsReflectionEnabledByDefault = false` will **break** JS interop (Blazor relies on reflection-based serialization).
- Optimized **byte-array** path — pass `byte[]` directly, no Base64 round-trip.
- After SignalR drops on Server, every `IJSObjectReference.Invoke*` and `Dispose{Async}` throws `JSDisconnectedException`. Catch and ignore in disposers.
- Server-side, JS→.NET payloads are bounded by `HubOptions.MaximumReceiveMessageSize` (default 32 KB). Adjust via `.AddInteractiveServerComponents().AddHubOptions(o => o.MaximumReceiveMessageSize = 1024 * 1024)`.
- Determining where you run: `if (OperatingSystem.IsBrowser()) { /* WASM */ }`.
- **Don't mutate DOM owned by Blazor with raw JS** — only touch elements Blazor isn't tracking.
- **Don't call `IJSRuntime` from `OnInitialized{Async}`.** During prerender there is no DOM. Place JS interop in `OnAfterRenderAsync(firstRender: true)`.
