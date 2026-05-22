# GC Fundamentals — Heap, Generations, Modes

## Heap layout

| Heap | Size class | Compacted | Collected with |
|---|---|---|---|
| **SOH** (gen0/1/2) | < 85 000 B | **Yes** (gen0/1 always; gen2 in blocking GCs and partial in BGC) | gen0 / gen1 / gen2 |
| **LOH** | ≥ 85 000 B (configurable via `System.GC.LOHThreshold`) | **No** by default; `LargeObjectHeapCompactionMode = CompactOnce` opts in once | gen2 |
| **POH** | Pinned via `GC.AllocateArray<T>(len, pinned: true)` | **Never** | gen2 |

Generations:

| Gen | Contains | Triggered by | Pause |
|---|---|---|---|
| 0 | New small objects. | Allocation pointer crosses gen0 budget. | Lowest. |
| 1 | Gen0 survivors (buffer between gen0 and gen2). | Promotion from gen0. | Low. |
| 2 | Long-lived; static-field graphs, loaded assemblies. | Memory pressure, gen1 promotion threshold, induced full GC. | High; full GC. |

Promotion: anything that survives a gen-N collection moves to gen-(N+1), capped at gen2. LOH/POH objects survive in their own heap.

## Roots

Stack locals & registers, static fields, GC handles (`Pinned`/`Weak`/`Normal`/`WeakTrackResurrection`), the **finalizer queue itself**.

## Workstation vs Server GC

| Dimension | Workstation (default) | Server |
|---|---|---|
| Heaps | One heap, one allocation context. | One heap **per logical processor** (configurable via `HeapCount`). |
| GC threads | User thread that triggers GC; concurrent flavor adds a background thread. | Dedicated GC thread per heap, typically `THREAD_PRIORITY_HIGHEST`. |
| Throughput | Lower. | Higher (parallel mark/sweep across heaps). |
| Footprint | Lower. | Higher (per-heap segments + per-heap allocation contexts). |
| Pause shape | Better on small heaps. | Better on large heaps; worst-case pauses on small heaps are larger. |
| Concurrent GC | On by default. | On by default (called **background server GC**). Each heap gets its own background thread. |

### Selecting the flavor

`runtimeconfig.json`:

```json
{
  "runtimeOptions": {
    "configProperties": {
      "System.GC.Server": true,
      "System.GC.Concurrent": true
    }
  }
}
```

MSBuild:

```xml
<PropertyGroup>
  <ServerGarbageCollection>true</ServerGarbageCollection>
  <ConcurrentGarbageCollection>true</ConcurrentGarbageCollection>
</PropertyGroup>
```

Env: `DOTNET_gcServer=1`, `DOTNET_gcConcurrent=1`.

Inspect at runtime:

```csharp
using System.Runtime;
Console.WriteLine($"IsServerGC = {GCSettings.IsServerGC}");
Console.WriteLine($"LatencyMode = {GCSettings.LatencyMode}");
Console.WriteLine($"LOHCompactionMode = {GCSettings.LargeObjectHeapCompactionMode}");
```

## Background / concurrent GC

Default since .NET 4.5. Background gen2 collections run **concurrently** with user threads; foreground gen0/1 (ephemeral) GCs can preempt the BGC. Two short blocking pauses bracket the concurrent mark; gen2 compaction still requires a blocking pause.

Disable only for deterministic pause testing or when bounding max heap with synchronous gen2: `"System.GC.Concurrent": false`.

| Collection | Typical pause |
|---|---|
| gen0 | sub-ms to a few ms |
| gen1 | slightly longer |
| gen2 background | two short pauses; mark concurrent |
| gen2 blocking | full STW; can be 100s of ms on multi-GB heaps |

## DATAS — Dynamic Adaptation To Application Sizes

Server-only. Lets the GC start with a small heap and grow only when sustained live data warrants it. Drastically reduces footprint for small services without measurable throughput loss.

| .NET | DATAS state |
|---|---|
| 7 | Opt-in (`DOTNET_GCDynamicAdaptationMode=1`). |
| 8 | **Default ON**. |
| 9 / 10 | Default ON; tuning improvements. |

Toggle:

```json
{ "runtimeOptions": { "configProperties": { "System.GC.DynamicAdaptationMode": 1 } } }
```

Env: `DOTNET_GCDynamicAdaptationMode=1`. Disable only with explicit benchmark justification or when you want pre-.NET-8 fixed-size behavior. Workstation GC ignores DATAS. `HeapHardLimit*` still caps the GC; DATAS adapts within that ceiling.

## Large Object Heap (LOH)

- Default threshold: **85 000 bytes** (configurable via `System.GC.LOHThreshold`). On 32-bit, `double[]` of length ≥ 1000 also goes to LOH for alignment.
- Allocation walks a free list; LOH is **never automatically compacted**.
- Collected only with **gen2** (full GC).
- Fragments under high churn.

On-demand compaction:

```csharp
using System.Runtime;
GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
GC.Collect();   // Next full GC compacts LOH; mode resets to Default.
```

Mitigations for chronic LOH churn: pool large buffers (`ArrayPool<byte>.Shared`, `RecyclableMemoryStream`); use `Span<T>`/`Memory<T>` slicing; read in 64 KB strides instead of whole files at once. Diagnose with `dotnet-counters` (`loh-size`, `gen-2-gc-count`), `dotnet-gcdump`, or PerfView "GC Heap Alloc Stacks".

## Pinned Object Heap (POH)

Dedicated heap for permanently-pinned buffers (interop, networking, crypto). Allocate via:

```csharp
byte[] buf  = GC.AllocateArray<byte>(length: 4096, pinned: true);            // zero-init
byte[] buf2 = GC.AllocateUninitializedArray<byte>(length: 4096, pinned: true); // skip zero-init
```

POH objects are **not moved** by any collection; address is stable for life. Collected with gen2; never compacted. Best for long-lived buffers; for short-lived pinning use `fixed` (no POH allocation).
