# Induced GC, Latency Modes, Weak Refs, Finalization

## Induced GC

```csharp
GC.Collect();                                                      // Full blocking gen2.
GC.Collect(2, GCCollectionMode.Forced);                            // Force regardless of heuristics.
GC.Collect(2, GCCollectionMode.Optimized);                         // GC may decline if it won't help.
GC.Collect(2, GCCollectionMode.Aggressive);                        // Reclaim every byte; expensive.
GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true); // Compact gen2 + LOH.
GC.WaitForPendingFinalizers();                                     // Drain finalizer queue.
```

| Mode | Meaning |
|---|---|
| `Default` | Same as `Forced` today. |
| `Forced` | Always perform the collection. |
| `Optimized` | GC may skip if reclamation wouldn't help. Recommended for "suggestion" calls. |
| `Aggressive` | Full + compacting + LOH compaction. Use only at well-defined idle points. |

Legitimate cases: non-recurring memory release (closed a giant document); after `AssemblyLoadContext.Unload` (`GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();`); benchmarks; tooling capturing snapshots. Do **not** call inside hot loops, request handlers, after every batch — you'll force gen2/LOH passes that would never have happened.

### `TryStartNoGCRegion`

```csharp
if (GC.TryStartNoGCRegion(totalSize: 16 * 1024 * 1024))
{
    try { /* latency-critical; allocations up to totalSize won't trigger GC */ }
    finally { GC.EndNoGCRegion(); }
}
else
{
    /* GC could not reserve the budget; fall back. */
}
```

Overloads accept `lohSize` and `disallowFullBlockingGC`. Returns `false` if the reservation fails. Exceeding the budget may still trigger GC and `EndNoGCRegion` may throw — size generously. Equivalent to `GCSettings.LatencyMode = NoGCRegion` (cannot be set directly; use the API).

### `GC.RegisterNoGCRegionCallback(long, Action)` (.NET 8+)

Schedule a callback when the no-GC region's reserved budget is exhausted.

## Full-GC notifications

Use case: a server can divert traffic before a long blocking gen2 GC. **Disable concurrent GC** for notifications to be useful.

```csharp
GC.RegisterForFullGCNotification(maxGenerationThreshold: 10, largeObjectHeapThreshold: 10);

new Thread(() =>
{
    while (true)
    {
        var s = GC.WaitForFullGCApproach();
        if (s == GCNotificationStatus.Succeeded) OnFullGCApproaching();
        else if (s is GCNotificationStatus.Canceled or GCNotificationStatus.NotApplicable) break;

        s = GC.WaitForFullGCComplete();
        if (s == GCNotificationStatus.Succeeded) OnFullGCComplete();
    }
}) { IsBackground = true }.Start();

GC.CancelFullGCNotification();
```

`GCNotificationStatus`: `Succeeded` / `Failed` / `Canceled` / `Timeout` / `NotApplicable` (concurrent/background GC is on). Threshold is *advisory*; the GC may still trigger sooner. Don't use for correctness.

## Latency modes

```csharp
using System.Runtime;
var prev = GCSettings.LatencyMode;
GCSettings.LatencyMode = GCLatencyMode.LowLatency;
try { /* ... */ } finally { GCSettings.LatencyMode = prev; }
```

| Mode | Effect | Typical use |
|---|---|---|
| `Batch` | Disables concurrent/background GC entirely. | Batch jobs, max throughput. |
| `Interactive` | Concurrent/background GC enabled. | Default for workstation. |
| `LowLatency` | **Workstation only.** Avoid gen2 except under memory pressure; transient. | UI animation, render frames — seconds, not minutes. |
| `SustainedLowLatency` | Workstation **and** server. Long-running low-latency; avoids blocking gen2 (heap may grow). App should opportunistically `GC.Collect(2, Optimized)` at idle. | Trading platforms, audio engines, game runtimes. |
| `NoGCRegion` | Set via `TryStartNoGCRegion` / `EndNoGCRegion`. | Hard windows. |

Rules: under memory pressure / OOM the runtime can still full-GC even in `LowLatency`; `Batch` is mostly orthogonal to `Concurrent` config; never leave a process in `LowLatency`/`NoGCRegion` permanently.

## Weak references

Two flavors:

| Flavor | Constructor | Tracks resurrection |
|---|---|---|
| Short | `new WeakReference<T>(obj)` | No (invalidated when target is finalized). |
| Long | `new WeakReference(obj, trackResurrection: true)` | Yes (valid until fully reclaimed). |

```csharp
WeakReference<Bitmap> wr = new(LoadBitmap());
if (wr.TryGetTarget(out var bmp)) UseBitmap(bmp);
else { bmp = LoadBitmap(); wr.SetTarget(bmp); }
```

Use for: caches that should not prevent collection; `ConditionalWeakTable<TKey,TValue>` for attached state; weak event managers. Don't use for lifetime correctness or in place of `IDisposable`.

## Finalization

A class with `~Type()` registers each instance into the **finalizer queue** at allocation. When the GC sees the instance is unreachable:

1. Move from finalizer queue to **f-reachable queue** (still rooted!). Object is **not** reclaimed in this collection.
2. The dedicated finalizer thread runs each finalizer.
3. Only the **next** collection of the right generation reclaims the (now-finalized) instance.

Consequences: finalizable objects survive at least one extra GC and get promoted (often to gen2); long finalizers stall the queue and build pressure; finalizers run on any thread except the allocator's; order is non-deterministic; static fields may already be reset; do not call into other finalizable objects.

Resurrection (re-rooting `this` inside the finalizer) is almost always a bad idea; mentioned only for completeness. `GC.ReRegisterForFinalize(this)` undoes `SuppressFinalize`.
