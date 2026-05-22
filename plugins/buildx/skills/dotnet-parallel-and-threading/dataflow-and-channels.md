# TPL Dataflow, Channels, and Concurrent Collections

## TPL Dataflow

Actor / pipeline model. Built into the framework (`System.Threading.Tasks.Dataflow`). Three categories: **buffering**, **execution**, **grouping**.

### Block taxonomy

| Block | Kind | Element |
|---|---|---|
| `BufferBlock<T>` | Buffering | FIFO queue; one consumer per message. |
| `BroadcastBlock<T>` | Buffering | Holds latest, offered to **all** linked targets. |
| `WriteOnceBlock<T>` | Buffering | Accepts first message, ignores rest. |
| `ActionBlock<TInput>` | Execution | Runs delegate per message (sync or `Func<T,Task>`). |
| `TransformBlock<TIn,TOut>` | Execution | 1 in → 1 out. |
| `TransformManyBlock<TIn,TOut>` | Execution | 1 in → 0..N out. |
| `BatchBlock<T>` | Grouping | Emits `T[]` of size N (greedy / non-greedy). |
| `JoinBlock<T1,T2>` / `<T1,T2,T3>` | Grouping | Emits `Tuple<...>` once each `.TargetN` has a message. |
| `BatchedJoinBlock<T1,T2>` / `<T1,T2,T3>` | Grouping | Emits `Tuple<IList<T1>, IList<T2>, …>` of given total size. |

Interfaces: `ISourceBlock<T>`, `ITargetBlock<T>`, `IPropagatorBlock<TIn,TOut>` (extends both), `IDataflowBlock` (`Complete()`, `Completion`, `Fault(ex)`).

### Posting / receiving

```csharp
target.Post(msg);                         // sync, returns bool
await target.SendAsync(msg, ct);          // backpressures
T x = await source.ReceiveAsync(ct);
bool ok = source.TryReceive(out T value);

// Choose: take from whichever source has data first.
int branch = await DataflowBlock.Choose(
    source1, v => HandleA(v),
    source2, v => HandleB(v));
```

### Linking + completion propagation

```csharp
var transform = new TransformBlock<int, string>(
    n => $"{n}^2 = {n * n}",
    new ExecutionDataflowBlockOptions
    {
        MaxDegreeOfParallelism = Environment.ProcessorCount,
        BoundedCapacity        = 1024,
        EnsureOrdered          = true,                  // default true
        CancellationToken      = ct
    });

var print = new ActionBlock<string>(
    s => Console.WriteLine(s),
    new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 1 });

transform.LinkTo(print, new DataflowLinkOptions
{
    PropagateCompletion = true,
    Append              = true,
    MaxMessages         = DataflowBlockOptions.Unbounded
});

for (int i = 0; i < 100; i++) await transform.SendAsync(i);
transform.Complete();
await print.Completion;
```

### Block options

| Class | Used by | Key properties |
|---|---|---|
| `DataflowBlockOptions` | All | `BoundedCapacity` (default `Unbounded`), `CancellationToken`, `TaskScheduler`, `MaxMessagesPerTask`, `EnsureOrdered`. |
| `ExecutionDataflowBlockOptions` | `ActionBlock`, `Transform[Many]Block` | adds `MaxDegreeOfParallelism` (**default 1** — must opt in), `SingleProducerConstrained`. |
| `GroupingDataflowBlockOptions` | `BatchBlock`, `JoinBlock`, `BatchedJoinBlock` | adds `Greedy` (default `true`), `MaxNumberOfGroups`. |

Execution-block delegates that throw fault the block (`Completion` task is `Faulted`) and discard subsequent messages. Wrap in try/catch inside the body if recovery is needed. `PropagateCompletion = true` on a link forwards both completion and faults.

## `System.Threading.Channels`

Lower-level, allocation-light producer/consumer pipe. Prefer over Dataflow when you want plain async streams without block topology.

```csharp
var bounded = Channel.CreateBounded<int>(new BoundedChannelOptions(capacity: 1024)
{
    SingleReader = true,
    SingleWriter = false,
    AllowSynchronousContinuations = false,
    FullMode = BoundedChannelFullMode.Wait    // Wait | DropNewest | DropOldest | DropWrite
});

var unbounded = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
{
    SingleReader = false,
    SingleWriter = true
});

var pri = Channel.CreateUnboundedPrioritized<Job>(
    new UnboundedPrioritizedChannelOptions<Job> { Comparer = JobPriorityComparer.Instance });
```

`SingleReader` / `SingleWriter = true` enables a fast lock-free path; setting them wrongly is a data-corruption bug.

```csharp
ChannelWriter<int> w = bounded.Writer;
ChannelReader<int> r = bounded.Reader;

await w.WriteAsync(42, ct);
if (w.TryWrite(43)) { /* fast path */ }
await w.WaitToWriteAsync(ct);
w.Complete();                               // mark end-of-stream
w.Complete(new InvalidOperationException()); // fault

await foreach (int item in r.ReadAllAsync(ct))
    Process(item);
```

`ReadAllAsync` returns `IAsyncEnumerable<T>` and stops cleanly when the channel completes. After completion, writes throw `ChannelClosedException`.

| Need | Use |
|---|---|
| Async producer/consumer with backpressure | `Channel<T>` |
| Sync blocking producer/consumer | `BlockingCollection<T>` over `ConcurrentQueue<T>` |
| Multi-stage pipeline, fan-out/in, batching, joining, scheduling | TPL Dataflow |

## `System.Collections.Concurrent`

| Type | Semantics | Notes |
|---|---|---|
| `ConcurrentDictionary<TKey,TValue>` | Thread-safe map. | Striped locks; lock-free reads. `valueFactory` may run multiple times under contention but only one wins. |
| `ConcurrentQueue<T>` | FIFO. | Lock-free. |
| `ConcurrentStack<T>` | LIFO. | Lock-free CAS. |
| `ConcurrentBag<T>` | Unordered. | Optimized for **same-thread produce-consume**; degraded otherwise. |
| `BlockingCollection<T>` | Bounded/unbounded blocking wrapper. | `Add`/`Take`, `CompleteAdding`, `GetConsumingEnumerable`. |

```csharp
var counts = new ConcurrentDictionary<string, int>();
counts.AddOrUpdate(key, addValue: 1, updateValueFactory: (_, old) => old + 1);
counts.GetOrAdd(key, k => Compute(k));
counts.TryUpdate(key, newValue: 5, comparisonValue: 4);
```
