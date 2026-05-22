---
name: dotnet-garbage-collection
description: Garbage collector reference for .NET 10. Covers fundamentals (generations, SOH/LOH/POH, roots, allocation), workstation vs server, background/concurrent GC, DATAS (default since .NET 8), induced collections (`GC.Collect`, `TryStartNoGCRegion`), full-GC notifications, latency modes, weak references and finalization, the canonical `IDisposable`/`IAsyncDisposable` patterns and `SafeHandle`, runtime configuration (`runtimeconfig.json` + MSBuild + `DOTNET_*` env vars: hard limits, heap count, LOH threshold, DATAS), the `System.GC`/`GCSettings` API, unmanaged-memory pressure hints, and `dotnet-counters`/`dotnet-gcdump`/`dotnet-trace`/PerfView diagnostics.
when_to_use: |
  - Trigger keywords: GC, generations, LOH, POH, server GC, background GC, DATAS, GC.Collect, TryStartNoGCRegion, GCSettings.LatencyMode, LargeObjectHeapCompactionMode, runtimeconfig.json, HeapHardLimitPercent, HeapCount, GCConserveMemory, finalizer, WeakReference, SafeHandle, IDisposable, IAsyncDisposable, AddMemoryPressure, dotnet-counters, dotnet-gcdump, OOMKilled.
  - Task shapes: choose workstation vs server GC; tune heap size for a container; chase a memory leak; diagnose LOH fragmentation; implement `Dispose`/`DisposeAsync`; declare a no-GC region; size `HeapHardLimitPercent` for K8s; switch latency modes; wrap unmanaged memory with pressure hints.
allowed-tools: Bash, Edit, Glob, Grep, Monitor, NotebookEdit, PowerShell, Read, Write
user-invocable: false
paths: ["**/*.cs", "**/*.csproj", "**/runtimeconfig.json", "**/runtimeconfig.template.json"]
---

# .NET Garbage Collection — Reference

Reference for understanding, configuring, and diagnosing the CoreCLR GC on .NET 10. The GC is automatic; this file is the dial-set you reach for when defaults aren't enough.

## Mental model

- The GC is a **tracing**, **generational**, **compacting** mark-sweep collector.
- Most allocations are bump-pointer in gen0; **most objects die in gen0**. The GC is tuned around that.
- Old objects rarely point to new objects → write barrier + card table let gen0/1 collections skip gen2 scans.
- The GC self-tunes; manual `GC.Collect` usually pessimizes throughput.
- A "memory leak" in managed code is a *root* you forgot — the GC won't reclaim rooted memory.

## Non-negotiable rules

1. **Don't call `GC.Collect()` on hot paths.** Defeats heuristics. Legitimate cases are non-recurring shape changes (closed a giant document, unloaded an `AssemblyLoadContext`, benchmarks).
2. **Implement `IDisposable`** when you own unmanaged or owned-managed resources. Make `Dispose` idempotent and thread-safe. Use the `Dispose(bool disposing)` pattern only when subclasses may extend the cleanup; sealed classes can omit it.
3. **`GC.SuppressFinalize(this)` belongs in the public `Dispose()`** after the cleanup, so the finalizer runs only when callers forgot.
4. **Prefer `SafeHandle` over `IntPtr` + finalizer.** It has a critical finalizer, ref counting (`DangerousAddRef`/`Release`), and composes with `IDisposable`.
5. **Never block in finalizers** and never touch other managed objects in `Dispose(false)` — they may already be finalized.
6. **In containers, set `HeapHardLimitPercent`** (e.g., 70–80). DATAS reduces but does not remove the need for a hard cap.
7. **Restore `LatencyMode`** in a `finally`. Never leave the process in `LowLatency` / `NoGCRegion` permanently.
8. **`GC.AddMemoryPressure` must be balanced** with one matching `RemoveMemoryPressure`. Leaking pressure makes the GC over-collect.

## Sub-index

| Topic | File | Load when |
|---|---|---|
| Heap layout, generations, roots, workstation vs server, BGC, DATAS, LOH, POH | [gc-fundamentals.md](gc-fundamentals.md) | Picking a GC flavor; understanding pause shape; sizing for a container; LOH/POH allocation. |
| `GC.Collect`, `TryStartNoGCRegion`, full-GC notifications, latency modes, weak references, finalization | [induced-gc-and-latency.md](induced-gc-and-latency.md) | Inducing a collection; carving a no-GC window; tuning latency modes; weak-ref caches; finalizer mechanics. |
| `IDisposable` / `IAsyncDisposable` / `SafeHandle` patterns | [dispose-pattern.md](dispose-pattern.md) | Authoring a class that owns native or managed resources. |
| `runtimeconfig.json` + MSBuild + env vars; memory pressure hints; `System.GC` and `GCSettings` API surface | [runtime-config-and-api.md](runtime-config-and-api.md) | Configuring heap caps, heap count, DATAS, LOH threshold; reading runtime info via `GC.*`. |
| `dotnet-counters` / `dotnet-gcdump` / `dotnet-trace` / PerfView; symptoms → fixes; `runtimeconfig` recipes; .NET 10 deltas | [diagnostics-and-recipes.md](diagnostics-and-recipes.md) | Diagnosing leaks, fragmentation, long pauses; copy-paste config for service / batch / desktop. |

## Cross-references

- Public docs (GC fundamentals): https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/fundamentals
- Public docs (Workstation vs Server GC): https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/workstation-server-gc
- Public docs (Background GC): https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/background-gc
- Public docs (LOH): https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/large-object-heap
- Public docs (Induced GC): https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/induced
- Public docs (Notifications): https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/notifications
- Public docs (Latency modes): https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/latency
- Public docs (Weak references): https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/weak-references
- Public docs (Implementing Dispose): https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/implementing-dispose
- Public docs (Implementing DisposeAsync): https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/implementing-disposeasync
- Public docs (Runtime config — GC): https://learn.microsoft.com/en-us/dotnet/core/runtime-config/garbage-collector
- Public docs (`System.GC`): https://learn.microsoft.com/en-us/dotnet/api/system.gc
- Public docs (`GCSettings`): https://learn.microsoft.com/en-us/dotnet/api/system.runtime.gcsettings
- Public blog (DATAS): https://devblogs.microsoft.com/dotnet/dynamic-adaptation-to-application-sizes/
- Related skill: `dotnet-asynchronous-programming` — `await using`, `IAsyncDisposable` consumer side.
- Related skill: `dotnet-io` — buffer pooling, `ArrayPool`, `Span<T>`, `Memory<T>` strategies.
- Related skill: `dotnet-native-interop` — `SafeHandle` authoring depth, P/Invoke marshalling.
- Related skill: `dotnet-diagnostics` — `EventSource`, `dotnet-counters`, OpenTelemetry.
