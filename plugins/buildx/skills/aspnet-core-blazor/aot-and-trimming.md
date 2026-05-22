# Performance, AOT, Trimming, Observability

WASM payload knobs (trimming, lazy assemblies, AOT), perf rules, OTel meters, GC notes. Load when shrinking WASM payloads, enabling AOT, or wiring observability.

## Performance rules

- **Avoid unnecessary renders**: override `ShouldRender` for read-only components; `@key` every dynamic loop child; use `EventCallback`/`EventCallback<T>` over raw `Action`/`Func`.
- **Virtualize** long lists.
- **Reduce JS interop chatter**: batch into a single `module.invoke*`; pass `byte[]` directly; on WASM use `IJSInProcessRuntime` or `JSImport`/`JSExport` for hot paths.
- **Streaming SSR**: `@attribute [StreamRendering]` flushes partial HTML during async initialization.
- **WASM payload knobs**: IL Trimming (`<PublishTrimmed>true</PublishTrimmed>`, default for WASM publish — guard with `<TrimmerRootAssembly>` and `[DynamicallyAccessedMembers]`), runtime relinking (needs `wasm-tools`), Brotli precompression, lazy assemblies.

## AOT + lazy assemblies

```xml
<PropertyGroup>
  <RunAOTCompilation>true</RunAOTCompilation>
  <WasmStripILAfterAOT>true</WasmStripILAfterAOT>
  <BlazorWebAssemblyJiterpreter>true</BlazorWebAssemblyJiterpreter>
</PropertyGroup>
<ItemGroup>
  <BlazorWebAssemblyLazyLoad Include="Heavy.Reports.dll" />
</ItemGroup>
```

Lazy-load wiring uses `@inject LazyAssemblyLoader` + `OnNavigateAsync` to load assemblies before route resolution.

**AOT** (`<RunAOTCompilation>true</RunAOTCompilation>`): 2-10× CPU win, 2-3× larger payload. Reserve for CPU-bound apps. Requires `dotnet workload install wasm-tools`.

## Observability (.NET 10)

```csharp
builder.Services.ConfigureOpenTelemetryMeterProvider(m =>
{
    m.AddMeter("Microsoft.AspNetCore.Components");
    m.AddMeter("Microsoft.AspNetCore.Components.Lifecycle");
    m.AddMeter("Microsoft.AspNetCore.Components.Server.Circuits");
});
builder.Services.ConfigureOpenTelemetryTracerProvider(t =>
{
    t.AddSource("Microsoft.AspNetCore.Components");
    t.AddSource("Microsoft.AspNetCore.Components.Server.Circuits");
});
```

Key meters: `aspnetcore.components.navigate`, `.handle_event.duration`, `.update_parameters.duration`, `.render_diff.duration`, `.render_diff.size`, `.circuit.active`, `.circuit.connected`, `.circuit.duration`. Activity sources: `StartCircuit`, `Navigate`, `HandleEvent`.

## Memory / GC

- WASM: single-threaded GC, bounded heap. Avoid hot-path allocation; prefer `Span<T>`, struct enumerators, `ArrayPool<T>`.
- Server: dispose scoped services owning unmanaged handles via `OwningComponentBase`.
- Don't put per-user state in singletons on the server — that's per-process. A circuit is the right scope.
- Cancel background work in `Dispose`/`DisposeAsync`.
