---
name: dotnet-parallel-and-threading
description: Parallelism, threading, and synchronization reference for .NET 10 / C# 14. Covers `Task`/TPL (creation, schedulers, `TaskCompletionSource`), data parallelism (`Parallel.For`/`ForEach`/`ForEachAsync`/`Invoke`), PLINQ, TPL Dataflow, producer/consumer (`Channels`, `BlockingCollection`), threads + thread pool, the .NET 9+ `System.Threading.Lock` and classic primitives (`Monitor`, `Mutex`, `SemaphoreSlim`, `ReaderWriterLockSlim`, `ManualResetEventSlim`, `Barrier`, `SpinLock`), `Interlocked`, the memory model + `volatile`, thread-local storage (`[ThreadStatic]`, `ThreadLocal<T>`, `AsyncLocal<T>`), and async-friendly sync patterns.
when_to_use: |
  - Trigger keywords: Parallel.ForEachAsync, PLINQ, AsParallel, Dataflow, ActionBlock, TransformBlock, Channel, ChannelReader, BlockingCollection, ConcurrentDictionary, ConcurrentQueue, ThreadPool, System.Threading.Lock, SemaphoreSlim, ReaderWriterLockSlim, Barrier, SpinLock, Interlocked, CompareExchange, volatile, ThreadLocal, AsyncLocal, TaskScheduler, TaskCompletionSource.
  - Task shapes: pick a parallelism API (data / task / declarative / pipeline / channel); throttle async fan-out; design a producer/consumer queue; choose a sync primitive; replace `lock(object)` with `Lock`; write a CAS retry; tune thread-pool min/max; build a Dataflow pipeline with backpressure.
allowed-tools: Bash, Edit, Glob, Grep, Monitor, NotebookEdit, PowerShell, Read, Write
user-invocable: false
paths: ["**/*.cs"]
---

# .NET Parallel Programming and Threading — Reference

Reference for picking the right parallelism API, synchronization primitive, and concurrency pattern on .NET 10. Defer single-await semantics and the TAP shape to `dotnet-asynchronous-programming`; this file owns the multi-thread story.

## Mental model

- **Async ≠ parallel.** Async is one thread driving many in-flight operations; parallel is many threads driving work simultaneously.
- All TPL types (Task, Parallel, PLINQ, Dataflow) schedule on `ThreadPool` by default via `TaskScheduler.Default`.
- Pick the highest-level API that fits the workload; drop down only when measured cost demands it.
- Locks are correctness tools; concurrent collections and `Interlocked` are usually faster and harder to misuse.

### Workload taxonomy

| Workload | Right tool |
|---|---|
| Single CPU job, async caller | `Task.Run` |
| N CPU iterations over an array | `Parallel.For` / `Parallel.ForEach` |
| N async I/O calls with throttle | `Parallel.ForEachAsync(items, opts, body)` |
| Declarative parallel query | PLINQ (`source.AsParallel()...`) |
| Multi-stage pipeline with backpressure / fan-out | TPL Dataflow |
| High-throughput async producer/consumer | `System.Threading.Channels` |
| Sync producer/consumer | `BlockingCollection<T>` over `ConcurrentQueue<T>` |
| Long-blocking dedicated worker | `Thread` (foreground if must keep process alive) or `Task.Factory.StartNew(..., LongRunning)` |

## Non-negotiable rules

1. **Lock on a private dedicated field.** Never `lock(this)`, `lock(typeof(T))`, `lock("string")`, `lock(stringLiteral)`. Prefer `private readonly System.Threading.Lock _gate = new();` on .NET 9+.
2. **No `await` inside `lock`.** Compiler enforces it. For async-friendly mutual exclusion use `SemaphoreSlim(1, 1).WaitAsync()`.
3. **Don't block the thread pool.** No `Thread.Sleep`, `task.Wait()`, `task.Result` on pool threads. For long blocking work use `TaskCreationOptions.LongRunning` or a dedicated `Thread`.
4. **`Interlocked` over `lock` for atomic counters / one-shot init.** `Increment`, `Add`, `CompareExchange<T>` are CPU-atomic and don't throw.
5. **Use `Environment.ProcessorCount`** as the scaling base; never hardcode core counts.
6. **`Thread.Abort` is gone.** SYSLIB0006 — replace with cooperative cancellation via `CancellationToken`.
7. **`ConcurrentBag` only for same-thread produce-consume.** Mismatched producer/consumer thread sets silently degrade to `ConcurrentQueue` performance with extra overhead.
8. **Set `TaskCreationOptions.RunContinuationsAsynchronously`** on every `TaskCompletionSource<T>` to prevent caller stack dives.

## Sub-index

| Topic | File | Load when |
|---|---|---|
| TPL `Task` low-level surface (creation, options, schedulers, `TaskCompletionSource`, child tasks, lifecycle) | [tasks-and-task-types.md](tasks-and-task-types.md) | Going below `await` — custom schedulers, `TaskCompletionSource`, `LongRunning`, `Unwrap`. |
| Data parallelism — `Parallel.For/ForEach/Invoke/ForEachAsync` and PLINQ | [parallel-and-plinq.md](parallel-and-plinq.md) | Fan out CPU iterations or async I/O with throttling; declarative parallel queries. |
| TPL Dataflow blocks, `System.Threading.Channels`, `System.Collections.Concurrent` | [dataflow-and-channels.md](dataflow-and-channels.md) | Producer/consumer, multi-stage pipelines, backpressure, batching, lock-free collections. |
| `Thread` and managed `ThreadPool` | [threads-and-pool.md](threads-and-pool.md) | Foreground/STA threads, `IThreadPoolWorkItem`, tune `SetMinThreads`. |
| `lock` / `System.Threading.Lock`, `Monitor`, `Mutex`, `SemaphoreSlim`, events, `ReaderWriterLockSlim`, `SpinLock` | [synchronization-primitives.md](synchronization-primitives.md) | Pick a sync primitive; cross-process gates; reader/writer; spin. |
| `Interlocked`, memory model + `volatile`, thread-local storage, async-friendly sync patterns | [interlocked-and-memory-model.md](interlocked-and-memory-model.md) | CAS, atomic counters, ordering, `[ThreadStatic]` / `ThreadLocal<T>` / `AsyncLocal<T>`, async mutex. |

## Quick decision matrix

| Goal | Use |
|---|---|
| Run one piece of CPU work async | `Task.Run` |
| Fan out N CPU iterations | `Parallel.For` / `Parallel.ForEach` |
| Fan out N async / I/O calls with throttle | `Parallel.ForEachAsync` |
| Declarative parallel data query | PLINQ (`AsParallel().Where(...)`) |
| Pipeline of stages with backpressure | TPL Dataflow |
| Async producer/consumer queue | `Channel<T>` |
| Sync producer/consumer queue | `BlockingCollection<T>` |
| Atomic counter | `Interlocked.Increment` |
| Atomic CAS | `Interlocked.CompareExchange` |
| Mutual exclusion (sync) | `lock` over `Lock` (.NET 9+) |
| Mutual exclusion (async) | `SemaphoreSlim(1, 1).WaitAsync()` |
| Throttle N concurrent | `SemaphoreSlim(N)` |
| Reader/writer | `ReaderWriterLockSlim` |
| One-shot signal | `ManualResetEventSlim` / `TaskCompletionSource` |
| Wait for N events | `CountdownEvent` |
| Multi-phase rendezvous | `Barrier` |
| Per-thread state | `ThreadLocal<T>` or `[ThreadStatic]` |
| Per-async-flow state | `AsyncLocal<T>` |
| Cross-process mutex / semaphore / event | `Mutex` / `Semaphore` / `EventWaitHandle` (named, Windows) |

## Cross-references

- Public docs (Parallel Programming index): https://learn.microsoft.com/en-us/dotnet/standard/parallel-programming/
- Public docs (Task Parallel Library): https://learn.microsoft.com/en-us/dotnet/standard/parallel-programming/task-parallel-library-tpl
- Public docs (Data Parallelism): https://learn.microsoft.com/en-us/dotnet/standard/parallel-programming/data-parallelism-task-parallel-library
- Public docs (`Parallel.ForEachAsync`): https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.parallel.foreachasync
- Public docs (TPL Dataflow): https://learn.microsoft.com/en-us/dotnet/standard/parallel-programming/dataflow-task-parallel-library
- Public docs (PLINQ): https://learn.microsoft.com/en-us/dotnet/standard/parallel-programming/introduction-to-plinq
- Public docs (`Channel<T>`): https://learn.microsoft.com/en-us/dotnet/api/system.threading.channels.channel-1
- Public docs (Managed thread pool): https://learn.microsoft.com/en-us/dotnet/standard/threading/the-managed-thread-pool
- Public docs (Synchronization primitives overview): https://learn.microsoft.com/en-us/dotnet/standard/threading/overview-of-synchronization-primitives
- Public docs (`ReaderWriterLockSlim`): https://learn.microsoft.com/en-us/dotnet/api/system.threading.readerwriterlockslim
- Public docs (`Interlocked`): https://learn.microsoft.com/en-us/dotnet/api/system.threading.interlocked
- Public docs (Thread-local storage): https://learn.microsoft.com/en-us/dotnet/standard/threading/thread-local-storage-thread-relative-static-fields-and-data-slots
- Public docs (`lock` statement / `System.Threading.Lock`): https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/lock
- Related skill: `dotnet-asynchronous-programming` — `async`/`await`, `Task` semantics, `ValueTask`, cancellation, `IAsyncEnumerable`.
- Related skill: `dotnet-io` — `System.IO.Pipelines`, parallel file I/O via `RandomAccess`.
- Related skill: `dotnet-networking` — fan-out HTTP, `HttpClient` concurrency, `IHttpClientFactory`.
