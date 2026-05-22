# GC Profiling, Anti-patterns, and Recipes

## `dotnet-counters`

```text
dotnet tool install --global dotnet-counters
dotnet-counters monitor --process-id <pid> System.Runtime
```

Key counters: `gen-{0,1,2}-gc-count`, `gen-{0,1,2}-size`, `loh-size`, `poh-size`, `gc-fragmentation`, `gc-heap-size`, `time-in-gc`, `alloc-rate`, `gc-committed`, `gc-pause-time`.

## `dotnet-gcdump`

```text
dotnet-gcdump collect --process-id <pid> --output myapp.gcdump
```

Open in Visual Studio Memory Profiler or PerfView. Forces a GC first to skip dead objects.

## `dotnet-trace`

```text
dotnet-trace collect --process-id <pid> --providers Microsoft-Windows-DotNETRuntime:0x1:5
```

Captures EventPipe traces (`GCStart_V2`, `GCEnd_V1`, `GCAllocationTick_V3`). Convert with `dotnet-trace convert --format speedscope`.

## PerfView (Windows)

GCStats report (counts, pause times, generation budgets, promoted bytes); GC Heap Alloc Stacks (sampled allocation attribution); take heap snapshot (~ `dotnet-gcdump`).

## In-proc `EventListener`

```csharp
sealed class GcListener : EventListener
{
    protected override void OnEventSourceCreated(EventSource es)
    {
        if (es.Name == "Microsoft-Windows-DotNETRuntime")
            EnableEvents(es, EventLevel.Verbose, (EventKeywords)0x1 /* GC */);
    }
    protected override void OnEventWritten(EventWrittenEventArgs e)
    {
        if (e.EventName is "GCStart_V2" or "GCEnd_V1" or "GCSuspendEEEnd_V1" or "GCRestartEEEnd_V1")
            Console.WriteLine($"{e.TimeStamp:O} {e.EventName} {string.Join(',', e.Payload ?? [])}");
    }
}
```

## Crash / dump analysis

- `DOTNET_DbgEnableMiniDump=1` + `DOTNET_DbgMiniDumpType=4` (heap) on OOM.
- `dotnet-dump analyze core.123.dmp` then `dumpheap -stat` / `gcroot <addr>` / `eeheap -gc`.

## Practical guidance / anti-patterns

| Symptom | Likely cause | Fix |
|---|---|---|
| Steady RSS growth, gen2 climbing | Leak: rooted cache, event-handler cycle, static collection. | `gcdump` + `gcroot`; weak refs / `ConditionalWeakTable`; unhook handlers. |
| LOH fragmentation, OOM with low live size | High-churn LOH allocations. | Pool buffers (`ArrayPool<byte>`, `RecyclableMemoryStream`); occasional `LargeObjectHeapCompactionMode = CompactOnce`. |
| Pinning fragmenting SOH | Long-lived `GCHandle.Alloc(o, Pinned)`. | Allocate on POH via `GC.AllocateArray<T>(len, pinned: true)`. |
| Long full-GC pauses on a server | BGC disabled or `LatencyMode = Batch`. | Re-enable concurrent GC; restore `Interactive`. |
| High `time-in-gc` % | Allocation rate too high. | Reduce allocations (struct/`Span<T>`/pooling); avoid LINQ chains in hot paths. |
| OOM in container despite low live heap | `HeapHardLimit*` not set. | Set `HeapHardLimitPercent` (~70). |
| Finalizer thread backlog | Long-running or blocking finalizers. | Move work to `Dispose`; finalize cheaply. |
| `LowLatency` left on, heap grows, full GC fires anyway | Fighting the GC. | Use `TryStartNoGCRegion` for hard windows. |

## Quick reference: minimal `runtimeconfig.json` recipes

### Latency-sensitive ASP.NET Core service in a container

```json
{
  "runtimeOptions": {
    "configProperties": {
      "System.GC.Server": true,
      "System.GC.Concurrent": true,
      "System.GC.HeapHardLimitPercent": 75,
      "System.GC.DynamicAdaptationMode": 1,
      "System.GC.RetainVM": false
    }
  }
}
```

### Throughput batch worker

```json
{
  "runtimeOptions": {
    "configProperties": {
      "System.GC.Server": true,
      "System.GC.Concurrent": false,
      "System.GC.RetainVM": true
    }
  }
}
```

### Memory-constrained desktop app

```json
{
  "runtimeOptions": {
    "configProperties": {
      "System.GC.Server": false,
      "System.GC.Concurrent": true,
      "System.GC.ConserveMemory": 5
    }
  }
}
```

### Hard real-time-ish window

```csharp
GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
// ... low-latency phase ...
GC.Collect(2, GCCollectionMode.Optimized);   // opportunistic cleanup at idle
GCSettings.LatencyMode = GCLatencyMode.Interactive;
```

## .NET 10 deltas

- DATAS still on by default for server GC; tuning improvements over .NET 9 reduce footprint further on small services.
- `GC.GetConfigurationVariables()` and `GC.RefreshMemoryLimit()` (.NET 8) remain canonical for inspecting / refreshing container limits at runtime.
- `GCKind`-aware `GC.GetGCMemoryInfo` distinguishes ephemeral / background / full collections in returned info.
- POH (.NET 5) and DATAS (.NET 8 default) are stable; no breaking changes in .NET 10.
