# Runtime Configuration, Memory Pressure, and `System.GC` API

## Runtime configuration

Settings live under `runtimeOptions.configProperties` in `runtimeconfig.json`. MSBuild equivalents inject the same property at publish time. Env vars override the file.

### Flavor and concurrency

| Key | MSBuild | Env | Default |
|---|---|---|---|
| `System.GC.Server` | `<ServerGarbageCollection>` | `DOTNET_gcServer` | `false` |
| `System.GC.Concurrent` | `<ConcurrentGarbageCollection>` | `DOTNET_gcConcurrent` | `true` |
| `System.GC.RetainVM` | `<RetainVMGarbageCollection>` | `DOTNET_GCRetainVM` | `false` |

### Heap size limits

| Key | Env | Effect |
|---|---|---|
| `System.GC.HeapHardLimit` | `DOTNET_GCHeapHardLimit` (hex bytes) | Hard cap on committed heap. |
| `System.GC.HeapHardLimitPercent` | `DOTNET_GCHeapHardLimitPercent` (1–100) | Hard cap as % of **container memory limit** (or physical memory). |
| `System.GC.HeapHardLimit{SOH,LOH,POH}[Percent]` | env | Per-section caps. |
| `System.GC.HighMemoryPercent` | `DOTNET_GCHighMemoryPercent` (1–99) | When system memory load crosses this %, GC becomes more aggressive about full collections. |

Either absolute or percent — not both for the same scope. In containers (cgroups v1/v2) the GC reads the container limit; `HeapHardLimitPercent` is relative to that, not the host. `GC.RefreshMemoryLimit()` re-reads after a container resize.

### Heap count / affinity

| Key | Env | Effect |
|---|---|---|
| `System.GC.HeapCount` | `DOTNET_GCHeapCount` (hex) | Pin number of heaps; default = logical CPUs. |
| `System.GC.NoAffinitize` | `DOTNET_GCNoAffinitize` | Don't pin GC threads to CPUs. |
| `System.GC.HeapAffinitizeMask` | `DOTNET_GCHeapAffinitizeMask` (hex) | Bitmask of allowed CPUs. |
| `System.GC.HeapAffinitizeRanges` | env | CPU range list (`0-3,8-11`). |
| `System.GC.CpuGroup` | `DOTNET_GCCpuGroup` | Windows CPU groups (>64 CPUs). |

### Thresholds and tuning

| Key | Env | Effect |
|---|---|---|
| `System.GC.LOHThreshold` | `DOTNET_GCLOHThreshold` (hex bytes, min 85 000) | Custom large-object threshold. |
| `System.GC.LargePagesEnabled` | `DOTNET_GCLargePages` | Use large/huge pages. |
| `System.GC.AllowVeryLargeObjects` | `<gcAllowVeryLargeObjects>` | Arrays > 2 GB. Default true on .NET Core+. |
| `System.GC.ConserveMemory` | `DOTNET_GCConserveMemory` (0–9) | Trade CPU for lower footprint; higher = more shrinking. |
| `System.GC.DynamicAdaptationMode` | `DOTNET_GCDynamicAdaptationMode` | DATAS toggle. |
| `System.GC.Name` | `DOTNET_GCName` | Path of an alternative standalone GC (experimental). |

### Container caveats

- `HeapHardLimitPercent` is the right knob for K8s pods — pin to ~70–80 so GC stays under the pod limit.
- DATAS reduces but doesn't remove the need for hard caps.
- `OOMKilled` despite low live heap typically means missing `HeapHardLimit*`.

## Unmanaged-memory pressure hints

When a managed object holds a large unmanaged buffer the GC can't see, balance every `Add` with one `Remove`:

```csharp
public sealed class NativeBlob : IDisposable
{
    private IntPtr _ptr;
    private readonly long _bytes;

    public NativeBlob(long bytes)
    {
        _bytes = bytes;
        _ptr = Marshal.AllocHGlobal((nint)bytes);
        GC.AddMemoryPressure(bytes);
    }

    public void Dispose()
    {
        if (_ptr != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_ptr);
            GC.RemoveMemoryPressure(_bytes);
            _ptr = IntPtr.Zero;
        }
        GC.SuppressFinalize(this);
    }

    ~NativeBlob() => Dispose();
}
```

Pass actual byte counts; don't use for tiny buffers. `HandleCollector` is a less-common pre-`SafeHandle` alternative for handle-count thresholds.

## `System.GC` API surface

| Member | Notes |
|---|---|
| `GC.MaxGeneration` | 2 today. |
| `GC.GetGeneration(object)` / `GetGeneration(WeakReference)` | LOH/POH report 2. |
| `GC.CollectionCount(int generation)` | Collections at gen N or higher. |
| `GC.GetTotalMemory(forceFullCollection)` | Bytes currently allocated. |
| `GC.GetTotalAllocatedBytes(precise = false)` | Cumulative allocations since process start. |
| `GC.GetAllocatedBytesForCurrentThread()` | Per-thread cumulative. |
| `GC.GetGCMemoryInfo()` / `GetGCMemoryInfo(GCKind)` | `HeapSizeBytes`, `MemoryLoadBytes`, `HighMemoryLoadThresholdBytes`, `TotalAvailableMemoryBytes`, `Index`, `Generation`, `FragmentedBytes`, `Compacted`, `Concurrent`, `PauseDurations`, `PauseTimePercentage`, `GenerationInfo[]`. |
| `GC.KeepAlive(object)` | Prevent early collection up to call site. |
| `GC.SuppressFinalize(this)` / `GC.ReRegisterForFinalize(this)` | Toggle finalizer membership. |
| `GC.AddMemoryPressure(long)` / `RemoveMemoryPressure(long)` | Unmanaged-memory hints. |
| `GC.AllocateArray<T>(int, bool pinned=false)` / `AllocateUninitializedArray<T>` | POH allocation. |
| `GC.TryStartNoGCRegion(...)` / `EndNoGCRegion()` | No-GC window. |
| `GC.RegisterForFullGCNotification(...)` / `WaitForFullGCApproach()` / `WaitForFullGCComplete()` / `CancelFullGCNotification()` | Notifications. |
| `GC.RefreshMemoryLimit()` (.NET 8+) | Re-read container limits. |
| `GC.GetConfigurationVariables()` | `IReadOnlyDictionary<string, object>` of GC config. |
| `GC.RegisterNoGCRegionCallback(long, Action)` (.NET 8+) | Callback when no-GC budget is exhausted. |
| `GC.GetTotalPauseDuration()` | Cumulative pause time. |

`System.Runtime.GCSettings`:

| Property | Notes |
|---|---|
| `GCSettings.IsServerGC` | Read-only flavor. |
| `GCSettings.LatencyMode` | Read/write. |
| `GCSettings.LargeObjectHeapCompactionMode` | Read/write; `Default` or `CompactOnce`. |
