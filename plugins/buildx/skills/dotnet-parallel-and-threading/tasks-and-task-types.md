# TPL — `Task` low-level surface

Async caller semantics live in `dotnet-asynchronous-programming`. This section covers what you need when you go below `await`.

## Creating tasks

| API | Use |
|---|---|
| `Task.Run(Action)` / `Task.Run(Func<TResult>)` | Default for thread-pool work. |
| `Task.Run(Func<Task>)` / `Task.Run(Func<Task<T>>)` | Async lambda — auto-unwraps. |
| `Task.Factory.StartNew(...)` | Need `TaskCreationOptions`, custom `TaskScheduler`, `state` arg. **Does not auto-unwrap** async lambdas — pair with `.Unwrap()`. |
| `Task.FromResult` / `FromException` / `FromCanceled` | Pre-completed tasks. `Task.CompletedTask` is a cached singleton. |

## `TaskCreationOptions`

| Flag | Semantics |
|---|---|
| `LongRunning` | Hints scheduler to allocate a dedicated thread (good for blocking work). |
| `AttachedToParent` | Child synchronizes completion + exceptions with parent. |
| `DenyChildAttach` | Children cannot attach (default for `Task.Run`). |
| `PreferFairness` | FIFO scheduling hint. |
| `HideScheduler` | Continuations use `TaskScheduler.Default` instead of inheriting current. |
| `RunContinuationsAsynchronously` | Forces async continuations — **always set on `TaskCompletionSource`**. |

## `TaskScheduler`

- `TaskScheduler.Default` — `ThreadPool`-backed; the workhorse.
- `TaskScheduler.Current` — scheduler of the running task (or `Default`).
- `TaskScheduler.FromCurrentSynchronizationContext()` — marshals continuations to UI / sync-context thread.
- `ConcurrentExclusiveSchedulerPair` — exposes `ConcurrentScheduler` (parallel readers) and `ExclusiveScheduler` (serialized writers); use for reader/writer task pipelines.

```csharp
var pair = new ConcurrentExclusiveSchedulerPair(
    TaskScheduler.Default,
    maxConcurrencyLevel: Environment.ProcessorCount);
var concurrent = new TaskFactory(pair.ConcurrentScheduler);
var exclusive  = new TaskFactory(pair.ExclusiveScheduler);
```

Custom schedulers override `QueueTask`, `TryExecuteTaskInline`, `GetScheduledTasks`.

## `TaskCompletionSource<T>`

```csharp
var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
RegisterCallback(value => tcs.TrySetResult(value));
int v = await tcs.Task;
```

## Child tasks (attached vs detached)

- Detached (default) — independent lifetime; exceptions don't flow to parent unless observed.
- `AttachedToParent` — parent's completion waits for the child; child exceptions wrap in parent's `AggregateException` (nested — call `.Flatten()`).
- `DenyChildAttach` defeats child attachment requests.

## `Task` lifecycle

`Created → WaitingForActivation → WaitingToRun → Running → (RanToCompletion | Faulted | Canceled)`. Optional `WaitingForChildrenToComplete` if attached children exist.
