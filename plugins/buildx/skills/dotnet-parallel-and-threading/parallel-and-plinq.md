# `Parallel` and PLINQ

## `Parallel` — data parallelism

```csharp
Parallel.For(0, items.Length, i => Process(items[i]));
Parallel.ForEach(source, item => Process(item));
Parallel.Invoke(() => DoA(), () => DoB(), () => DoC());
```

### Options

```csharp
var opts = new ParallelOptions
{
    MaxDegreeOfParallelism = Environment.ProcessorCount,
    CancellationToken      = cts.Token,
    TaskScheduler          = TaskScheduler.Default
};
```

`MaxDegreeOfParallelism = -1` (default) ⇒ unbounded; `1` ⇒ effectively sequential. For I/O-bound work, going above core count is legitimate.

### Stop vs Break

- `state.Stop()` — best-effort halt of every iteration; `IsCompleted` becomes `false`. Use for "search and exit".
- `state.Break()` — halt iterations with index >= current; lower-indexed iterations may still run. Use to preserve sequential `break` semantics.

### Thread-local accumulator (avoid lock contention)

```csharp
long total = 0;
Parallel.For<long>(
    fromInclusive: 0,
    toExclusive: data.Length,
    localInit:    () => 0L,
    body:         (i, state, local) => local + data[i],
    localFinally: local => Interlocked.Add(ref total, local));
```

### `Parallel.ForEachAsync`

Body returns `ValueTask`. Iterates `IEnumerable<T>` or `IAsyncEnumerable<T>`. Default `MaxDegreeOfParallelism` = `Environment.ProcessorCount`. The body's per-iteration token is **linked** to the outer `ParallelOptions.CancellationToken`.

```csharp
await Parallel.ForEachAsync(
    urls,
    new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct },
    async (url, token) =>
    {
        using var resp = await http.GetAsync(url, token);
        await ProcessAsync(resp, token);
    });
```

This is the modern way to fan out async I/O with throttling. Replaces `Task.WhenAll(items.Select(async ...))` whenever you need to cap concurrency.

### Exceptions

If iterations throw, remaining scheduled iterations may still run, then `Parallel.For/ForEach` raises an `AggregateException`. Always `.Flatten()` before inspecting.

### Custom partitioner

```csharp
var part = Partitioner.Create(0, data.Length, rangeSize: 4096);
Parallel.ForEach(part, range =>
{
    for (int i = range.Item1; i < range.Item2; i++) Process(data[i]);
});
```

`Partitioner.Create(IEnumerable<T>, EnumerablePartitionerOptions.NoBuffering)` disables chunk buffering for streaming sources.

## PLINQ — `ParallelEnumerable`

| Operator | Purpose |
|---|---|
| `AsParallel()` / `AsSequential()` | Enter / leave PLINQ. |
| `AsOrdered()` / `AsUnordered()` | Default = unordered (cheaper). `AsOrdered` buffers + sorts. |
| `WithDegreeOfParallelism(int)` | Cap workers (default = all cores). |
| `WithExecutionMode(ParallelExecutionMode)` | `Default` (heuristic) or `ForceParallelism` (override safety opt-out). |
| `WithCancellation(CancellationToken)` | Cooperative cancellation. |
| `WithMergeOptions(ParallelMergeOptions)` | `NotBuffered` (stream), `AutoBuffered` (default), `FullyBuffered`. |
| `ForAll(Action<T>)` | Terminal side-effect, no merge back to caller thread. |
| `Aggregate(seedFactory, accum, combine, projection)` | Local-then-combine reduce. |

```csharp
int sumOfSquares = data.AsParallel().Aggregate(
    seedFactory:              () => 0,
    updateAccumulatorFunc:    (acc, x) => acc + x * x,
    combineAccumulatorsFunc:  (a, b) => a + b,
    resultSelector:           x => x);
```

Don't parallelize: tiny element counts with cheap delegates; queries with order-sensitive operators (`ElementAt`, `TakeWhile`) without `AsOrdered`; bodies touching unprotected shared mutable state.

## Default `MaxDegreeOfParallelism`

| API | Default DOP |
|---|---|
| `Parallel.For` / `Parallel.ForEach` | -1 (TPL decides; usually ~ core count) |
| `Parallel.ForEachAsync` | `Environment.ProcessorCount` |
| PLINQ | up to `ProcessorCount` (heuristic) |
| Dataflow execution blocks | **1** (must set explicitly) |
