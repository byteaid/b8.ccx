---
name: dotnet-asynchronous-programming
description: Asynchronous programming reference for .NET 10 / C# 14. Covers the TAP shape (`async`/`await`, `Task` / `Task<T>` / `ValueTask`), state-machine semantics, return-type selection, exception propagation, `ConfigureAwait` and synchronization context, cancellation (`CancellationToken`, linked sources, `OperationCanceledException`), `IAsyncEnumerable<T>` + `await foreach`, `IAsyncDisposable` + `await using`, the canonical patterns and anti-patterns (no `async void` outside event handlers, no `.Result`/`.Wait()`, no fire-and-forget without observation), and the .NET 10 deltas worth knowing.
when_to_use: |
  - Trigger keywords: async, await, Task, Task<T>, ValueTask, ConfigureAwait, CancellationToken, IAsyncEnumerable, await foreach, IAsyncDisposable, await using, async void, Task.Run, Task.WhenAll, Task.WhenAny, Task.WhenEach, OperationCanceledException, deadlock, sync-over-async, state machine, async stream.
  - Task shapes: write an async method; pick `Task` vs `ValueTask`; thread a `CancellationToken`; choose `WhenAll` vs `WhenAny` vs `WhenEach`; convert sync code to async; eradicate `async void` / `.Result` / `.Wait()`; debug a deadlock; consume an async stream; author one with `yield return`.
allowed-tools: Bash, Edit, Glob, Grep, Monitor, NotebookEdit, PowerShell, Read, Write
user-invocable: false
paths: ["**/*.cs"]
---

# .NET Asynchronous Programming — Reference

Reference for authoring and reviewing async code on .NET 10 / C# 14. Pin the rules; defer the long catalogues to the Microsoft docs cited at the bottom.

## Mental model

- Async ≠ parallel. One thread can drive many in-flight async operations cooperatively.
- Async is .NET's **Promise/Future** model. `Task` / `Task<T>` are promises.
- **If any part of an operation is async, the whole call chain is async.** Don't mix sync and async — pick one.
- The compiler rewrites every `async` method into a state machine (`IAsyncStateMachine`); `await` is a suspend/resume point, not a thread block.
- `await` does **not** create threads. CPU work needs `Task.Run`; I/O work uses native completion ports — no extra thread.

### Workload taxonomy

| Workload | Right tool |
|---|---|
| I/O-bound (network, disk, DB) | `async`/`await` + the provider's `*Async` API. **Never** wrap in `Task.Run`. |
| CPU-bound, single call | `await Task.Run(() => Compute())` — only at the outermost frame. |
| CPU-bound, partitionable | `Parallel.For` / `Parallel.ForEachAsync` / PLINQ — load `dotnet-parallel-and-threading`. |
| Fan-out + aggregate I/O | Start tasks, then `Task.WhenAll` / `WhenEach`. |
| Fire-and-forget background work | `IHostedService` (load `dotnet-extensions`); never `async void`. |

## Non-negotiable rules

1. **TAP shape.** One method per operation, returns `Task` / `Task<T>` / `ValueTask` / `ValueTask<T>`, name suffixed with `Async`, optional trailing `CancellationToken token = default`. Argument-validation throws synchronously (before the first `await`); operational failures complete the task as faulted.
2. **No `async void`** except for event handlers (`async void OnClick(...)`). Anywhere else: returns `Task`, exceptions are observable, and the method is awaitable.
3. **No sync-over-async** (`.Result`, `.Wait()`, `.GetAwaiter().GetResult()`). It deadlocks under any captured `SynchronizationContext` (UI threads, legacy ASP.NET) and wastes a thread under `ThreadPool` schedulers.
4. **Forward `CancellationToken`** to every cancellable downstream call (CA2016). Never ignore the token a caller passed in.
5. **`ConfigureAwait(false)` in libraries.** Application code (controllers, services bound to a `SynchronizationContext`) may omit it; reusable libraries must add it on every `await` to avoid forcing context capture.
6. **`Task.WhenAll` rethrows the first exception synchronously.** Inspect `Task.Exception` after `WhenAll` if you need every inner exception.
7. **`ValueTask` is single-await.** Awaiting it twice, calling `.Result` twice, or storing it across the boundary is undefined behavior. Use `Task` unless profiling shows the allocation matters.

## `async` / `await` semantics

| Aspect | Behavior |
|---|---|
| `async` modifier | Enables `await` in the body. Allowed on methods, lambdas, anonymous methods. |
| `async` without `await` | Compiler warning; runs synchronously. State machine still built. |
| `await` of a completed task | Continues synchronously (fast path), no suspension. |
| `await` of a faulted task | Rethrows the **first** inner exception (not `AggregateException`). |
| `await` placement | Allowed in `try`/`catch`/`finally`, lambda bodies. C# 13+ relaxes some `ref`/`unsafe` restrictions. Never across a `lock` boundary. |
| `await x` type | `T` if `x` is `Task<T>`/`ValueTask<T>`/awaitable producing `T`; expression-statement when `x` is `Task`/`ValueTask`. |

A type is awaitable if it exposes `GetAwaiter()` returning a struct/class with `IsCompleted`, `GetResult()`, and at least `INotifyCompletion` (`ICriticalNotifyCompletion` preferred). `Task`, `Task<T>`, `ValueTask`, `ValueTask<T>`, `YieldAwaitable` (`Task.Yield()`), and `IAsyncEnumerator<T>.MoveNextAsync()` all conform.

## Return types — pick by use

| Return type | Use for | Notes |
|---|---|---|
| `Task` | Async method, no return value | Default for `void` operations. |
| `Task<T>` | Async method returning a value | Default for value operations. |
| `ValueTask` | Hot path likely-synchronous void | **Single-await.** Profile first. |
| `ValueTask<T>` | Hot path returning a value, often sync | Avoids `Task<T>` heap allocation when the result is cached/sync. |
| `void` | **Only** event handlers | Cannot be awaited. Exceptions tear down the process. |
| `IAsyncEnumerable<T>` | Async stream producer (`yield return` in `async`) | Consumed via `await foreach`. |
| `IAsyncEnumerator<T>` | Manually authored enumerator | Implements `IAsyncDisposable`. |

`ValueTask` rules of engagement (must be respected, otherwise undefined behavior):
- Await it exactly once.
- Don't pass it to `Task.WhenAll` / `WhenAny` directly; convert to `Task` first via `.AsTask()`.
- Don't store it as a field. Don't compare for equality.

When in doubt, return `Task` / `Task<T>`.

## Cancellation

```csharp
public async Task<List<Order>> GetOrdersAsync(int userId, CancellationToken ct = default)
{
    using var http = _factory.CreateClient("orders");
    var resp = await http.GetAsync($"/users/{userId}/orders", ct);
    ct.ThrowIfCancellationRequested();
    return await resp.Content.ReadFromJsonAsync<List<Order>>(ct) ?? [];
}
```

| Pattern | Use |
|---|---|
| `ct.ThrowIfCancellationRequested()` | Check between work units. |
| `ct.Register(callback)` | Trigger a cleanup when cancellation fires. |
| `CancellationTokenSource.CreateLinkedTokenSource(a, b)` | Cancel when **either** input cancels. |
| `cts.CancelAfter(timeout)` | Combined deadline + manual cancellation. |
| `OperationCanceledException` (or `TaskCanceledException`) | Catch only when the cancellation is yours, then re-throw. |

Do not swallow `OperationCanceledException` to convert it into a "no work" result — callers expect cancellation to propagate.

## `ConfigureAwait` — when context capture matters

| Caller context | Effect of `await` (default) | When to use `ConfigureAwait(false)` |
|---|---|---|
| WinForms / WPF UI thread | Continuation marshals back to UI thread. | Inside libraries that don't touch UI. |
| Legacy ASP.NET (System.Web) | Captures `AspNetSynchronizationContext`. | Inside libraries; also inside ASP.NET Core if portability matters. |
| ASP.NET Core / console / `IHostedService` | No `SynchronizationContext`; no-op. | Optional; some teams add it for cross-target consistency. |
| MAUI UI thread | Marshals back. | Inside libraries that don't touch UI. |

Rule of thumb: **library code adds `ConfigureAwait(false)` on every `await`; application code may omit it.**

`ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing)` (.NET 8+) silently ignores faults — only acceptable in fire-and-forget *internal* paths, never on a method whose completion the caller observes.

## Composition

| Operator | Returns when | Notes |
|---|---|---|
| `Task.WhenAll(tasks)` | All complete (or any fault). | Rethrows first exception; inspect `Task.Exception` for the rest. |
| `Task.WhenAny(tasks)` | Any one completes. | Use to race a primary against a timeout. |
| `Task.WhenEach(tasks)` (.NET 9+) | Yields each task as it completes via `IAsyncEnumerable<Task>`. | Replace `Task.WhenAny` loops with this. |
| `Task.Delay(ts, ct)` | Time elapses or `ct` cancels. | Always pass the token; otherwise cancellation has to wait the full delay. |
| `Task.Yield()` | Forces an asynchronous continuation. | Useful for cooperative scheduling in tight loops. |
| `Task.FromResult(value)` / `FromException(ex)` / `FromCanceled(ct)` | Synchronous factory. | Cheaper than allocating an `async` state machine for trivially-completed cases. |

```csharp
// Race a long-running call against a 5 s deadline.
var work = LongCallAsync(ct);
var deadline = Task.Delay(TimeSpan.FromSeconds(5), ct);
var winner = await Task.WhenAny(work, deadline);
if (winner == deadline) throw new TimeoutException();
return await work;
```

## Async streams — `IAsyncEnumerable<T>`

Producer:

```csharp
public async IAsyncEnumerable<int> ProduceAsync(
    [EnumeratorCancellation] CancellationToken ct = default)
{
    for (var i = 0; i < 100; i++)
    {
        await Task.Delay(50, ct);
        yield return i;
    }
}
```

Consumer:

```csharp
await foreach (var item in ProduceAsync(ct).WithCancellation(ct).ConfigureAwait(false))
    Process(item);
```

Notes:
- `[EnumeratorCancellation]` lets callers pass a token via `WithCancellation(ct)`. Without it, the producer cannot observe consumer-side cancellation.
- `ConfigureAwait(false)` is applied to the awaitable returned by `MoveNextAsync` — same library-vs-app rule applies.
- Don't expose synchronous `IEnumerable<T>` over a stream you actually `await` inside; convert to async and let consumers `await foreach`.

## `IAsyncDisposable` — `await using`

```csharp
public sealed class Connection : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        await CloseChannelAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}

await using var c = new Connection();
```

When a type implements both `IDisposable` and `IAsyncDisposable`, prefer `await using` in async code paths. The async dispose runs the proper async cleanup (e.g. flush a buffered stream); the sync `Dispose` is the fallback for sync callers and rarely the right path.

## Patterns and anti-patterns

| ✅ Pattern | ❌ Anti-pattern | Reason |
|---|---|---|
| `await DoAsync()` | `DoAsync().Result` / `.Wait()` / `.GetAwaiter().GetResult()` | Sync-over-async deadlocks under captured contexts; wastes a thread. |
| `async Task X()` | `async void X()` (non-event) | `void` makes the method un-awaitable and exceptions un-observable. |
| `Task.WhenAll(seq.Select(DoAsync))` | `foreach (var x in seq) await DoAsync(x)` (when independent) | Sequential awaits forfeit concurrency. |
| `await Task.Yield()` | `await Task.Run(() => syncCall())` (purely to avoid blocking) | `Run` schedules a thread-pool work item — only useful for true CPU-bound work. |
| Forward `ct` to every async call | Drop `ct` at any boundary | Cancellation must reach the leaf to be effective. |
| `await using var x = ...` | `using var x = ...` (with async cleanup) | Sync `Dispose` runs the wrong cleanup path. |
| `IAsyncEnumerable<T>` for streams | `IEnumerable<Task<T>>` | The consumer can't decide cancellation/concurrency cleanly. |
| Library: `await x.ConfigureAwait(false)` | Library: bare `await x` | Forces context capture in callers that don't want it. |

## CPU-bound work — `Task.Run`

```csharp
// ONLY for genuinely CPU-bound work. Push the offload as far out as possible.
var encoded = await Task.Run(() => Encode(input), ct);
```

- `Task.Run` schedules a delegate on the ThreadPool. Use exactly once at the top of a CPU-bound chain.
- **Never** wrap an already-async I/O method in `Task.Run` — you lose the I/O completion benefits and burn a thread.
- For partitioned work (loops over independent items), use `Parallel.ForEachAsync` instead — see `dotnet-parallel-and-threading`.

## Exception handling

- `await` rethrows the first exception that faulted the awaited task.
- `Task.WhenAll`'s aggregate is exposed via `task.Exception` (an `AggregateException`); the `await` rethrows only the first inner.
- `OperationCanceledException` is the canonical signal for cancellation; catching `Exception` to log and swallowing it loses the cancellation contract.
- Exceptions in `async void` methods propagate to the captured `SynchronizationContext`; with no context they reach `AppDomain.UnhandledException` and crash the process.
- `TaskScheduler.UnobservedTaskException` fires when a faulted task is GC'd without ever being awaited. Subscribe in `Program.cs` to log instead of swallow:

```csharp
TaskScheduler.UnobservedTaskException += (s, e) =>
{
    logger.LogError(e.Exception, "Unobserved task exception");
    e.SetObserved();
};
```

## Hot-path tips

- Synchronously completed `Task` is cached: `Task.CompletedTask`, `Task.FromResult(false)` / `(true)` / `(0)` reuse singletons. Returning these doesn't allocate.
- Re-use a `Task` via `Task.AsValueTask()` only when the same `Task` is awaited once.
- `[AsyncMethodBuilder(typeof(...))]` lets you swap the state-machine builder for a custom one (e.g. `AsyncStateMachineBox` pooling). Reach for it only after profiling shows the default builder is the bottleneck.
- `ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext)` is the explicit equivalent of the default — useful to make intent obvious.
- `ValueTask<T>.Preserve()` (.NET 5+) returns a `ValueTask<T>` you can await multiple times — but it allocates. Defeats the point unless really needed.

## .NET 10 / C# 14 deltas worth knowing

- `Task.WhenEach` (added .NET 9) is now the recommended replacement for `WhenAny` loops.
- `await using` / `await foreach` ergonomics in pattern-matched `using`-style local functions.
- `ConfigureAwaitOptions` enum (`ContinueOnCapturedContext`, `SuppressThrowing`, `ForceYielding`) — the bool overload is still supported.
- `SearchValues<T>`, `Lock`, and other concurrency primitives (covered in `dotnet-parallel-and-threading`).
- `[OverloadResolutionPriority]` improves which `*Async` overload binds for ambiguous call sites.

## Quick decision matrix

| Question | Answer |
|---|---|
| New API, returns nothing | `Task` |
| New API, returns a value | `Task<T>` |
| Hot path, almost always synchronous, allocations measured | `ValueTask` / `ValueTask<T>` |
| Need to expose a stream | `IAsyncEnumerable<T>` |
| Need to expose async cleanup | `IAsyncDisposable` |
| Need to fire-and-forget background work | `IHostedService`, not `async void` |
| Need to cancel | Add `CancellationToken token = default` parameter, forward to every async call |
| Library code | `ConfigureAwait(false)` on every `await` |
| Application/UI code | Bare `await` |
| Pivoting CPU work off the hot thread | `Task.Run(() => Compute(), ct)` at the outermost frame |
| Aggregating concurrent I/O | `Task.WhenAll(tasks)` (or `WhenEach` to stream completions) |

## Cross-references

- Public docs (Async overview): https://learn.microsoft.com/en-us/dotnet/csharp/asynchronous-programming/
- Public docs (TAP): https://learn.microsoft.com/en-us/dotnet/standard/asynchronous-programming-patterns/task-based-asynchronous-pattern-tap
- Public docs (`Task.WhenEach`): https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.task.wheneach
- Public docs (`ConfigureAwait` FAQ): https://devblogs.microsoft.com/dotnet/configureawait-faq/
- Public docs (`IAsyncEnumerable<T>`): https://learn.microsoft.com/en-us/dotnet/csharp/asynchronous-programming/generate-consume-asynchronous-stream
- Public docs (`IAsyncDisposable`): https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/implementing-disposeasync
- Public docs (`CancellationToken`): https://learn.microsoft.com/en-us/dotnet/standard/threading/cancellation-in-managed-threads
- Related skill: `dotnet-parallel-and-threading` — CPU parallelism, Channels, locks, PLINQ, Dataflow.
- Related skill: `dotnet-conventions` § csharp-style/async-hygiene — team async hygiene rules.
- Related skill: `dotnet-conventions` § csharp-style/valuetask — when `ValueTask` is acceptable on this team.
