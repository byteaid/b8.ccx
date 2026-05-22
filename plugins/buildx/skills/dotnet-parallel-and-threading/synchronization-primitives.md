# Synchronization Primitives

## `lock` and `System.Threading.Lock` (.NET 9+)

Classic lowering:

```csharp
private readonly object _gate = new();
lock (_gate) { /* critical section */ }
// ⇒ Monitor.Enter(__o, ref __taken); try { … } finally { if (__taken) Monitor.Exit(__o); }
```

`System.Threading.Lock` (.NET 9 / C# 13) — dedicated, more efficient mutual-exclusion type. When the compiler sees `lock (x)` with `x` of type `Lock`, it lowers to `using (x.EnterScope()) { … }`. `EnterScope()` returns a `ref struct` whose `Dispose` releases the lock — guaranteed cleanup, no boxing.

```csharp
public sealed class Account
{
    private readonly System.Threading.Lock _gate = new();
    private decimal _balance;

    public void Credit(decimal amount)
    {
        lock (_gate) { _balance += amount; }
    }

    public bool TryDebit(decimal amount)
    {
        using (_gate.EnterScope())
        {
            if (_balance < amount) return false;
            _balance -= amount;
            return true;
        }
    }
}
```

The compiler warns when a `Lock` instance is converted to `object` and used as a `lock` target — that defeats the optimization. `Lock` also exposes `Enter()`, `TryEnter(TimeSpan|int)`, `Exit()` for advanced use.

Pitfalls:
- Lock on a **dedicated, private, readonly** instance.
- Never `lock(this)`, `lock(typeof(T))`, `lock("string")`.
- Hold for as little time as possible; never call user code or do I/O under lock.
- `await` is forbidden inside `lock`; use `SemaphoreSlim`.
- `lock` is reentrant; design out reentrancy when possible.

## `Monitor`

Underpins `lock`. Use directly only for `TryEnter(timeout)` or `Wait`/`Pulse` condition variables.

- `Monitor.Enter(obj)`, `Enter(obj, ref bool lockTaken)` — mandatory pattern for guaranteed release.
- `Monitor.TryEnter(obj[, timeout][, ref taken])`.
- `Monitor.Wait(obj[, timeout])`, `Monitor.Pulse(obj)`, `Monitor.PulseAll(obj)` — caller must own the lock.
- `Monitor.IsEntered(obj)`.

```csharp
lock (_queueLock)
{
    while (_queue.Count == 0) Monitor.Wait(_queueLock);
    var item = _queue.Dequeue();
}
// producer:
lock (_queueLock) { _queue.Enqueue(x); Monitor.PulseAll(_queueLock); }
```

## `Mutex`, `Semaphore`, `EventWaitHandle` — cross-process

| Primitive | Cross-process (named) | Awaitable |
|---|---|---|
| `Mutex` | yes (Windows) | no |
| `Semaphore` | yes (Windows) | no |
| `SemaphoreSlim` | no | yes |
| `EventWaitHandle` | yes (Windows) | no |

```csharp
// System-wide singleton gate
using var m = new Mutex(initiallyOwned: false, name: @"Global\MyApp.Single", out bool createdNew);
if (!createdNew && !m.WaitOne(TimeSpan.Zero)) { Console.WriteLine("Already running"); return; }
try { /* run */ } finally { m.ReleaseMutex(); }
```

`Mutex` has thread affinity (the owning thread must release). `AbandonedMutexException` is thrown if the prior owner exited without releasing.

### `SemaphoreSlim` async throttling

```csharp
var gate = new SemaphoreSlim(initialCount: 8, maxCount: 8);
await Parallel.ForEachAsync(items, async (item, ct) =>
{
    await gate.WaitAsync(ct);
    try { await DownloadAsync(item, ct); }
    finally { gate.Release(); }
});
```

Releasing too many times throws `SemaphoreFullException`. No thread affinity — any thread may release.

## Events & rendezvous

| Type | Reset | Notes |
|---|---|---|
| `AutoResetEvent` | Auto (releases one waiter, then resets) | Turnstile. Use `EventWaitHandle` for named. |
| `ManualResetEvent` | Manual (`Reset()` to unsignal) | Gate. |
| `ManualResetEventSlim` | Manual | Lightweight; spins; supports `CancellationToken`; **no** cross-process. |
| `EventWaitHandle` | Either, **named** | Cross-process on Windows. |
| `CountdownEvent` | Becomes signaled when count hits zero | Fan-in across N events. `AddCount`, `Reset`. |
| `Barrier` | Phased rendezvous of fixed group | `SignalAndWait`; `postPhaseAction` runs on last arriver before any waiter is released. |

```csharp
// Fan-in N
using var done = new CountdownEvent(initialCount: items.Length);
foreach (var item in items)
    ThreadPool.QueueUserWorkItem(_ => { try { Process(item); } finally { done.Signal(); } });
done.Wait();
```

## `ReaderWriterLockSlim`

Multiple concurrent readers, single exclusive writer; optional upgradable read.

```csharp
private readonly ReaderWriterLockSlim _rw = new();   // default LockRecursionPolicy.NoRecursion (preferred)

public T Get(int key)
{
    _rw.EnterReadLock();
    try { return _store[key]; } finally { _rw.ExitReadLock(); }
}

public void Update(int key, T value)
{
    _rw.EnterUpgradeableReadLock();
    try
    {
        if (!_store.ContainsKey(key))
        {
            _rw.EnterWriteLock();
            try { _store[key] = value; } finally { _rw.ExitWriteLock(); }
        }
    }
    finally { _rw.ExitUpgradeableReadLock(); }
}
```

The legacy `ReaderWriterLock` (no Slim) — do not use in new code.

## `SpinLock` & `SpinWait`

- `SpinLock` (struct) — busy-spin mutex; useful only when the protected region is **microseconds**. Always pass by `ref`. Tracking-mode (default) detects ownership; non-tracking is fastest.
- `SpinWait` (struct) — `SpinOnce()` ramps from spinning → `Sleep(0)` → `Sleep(1)`; `SpinUntil(Func<bool>)`.

```csharp
SpinLock sl = new(enableThreadOwnerTracking: false);
bool taken = false;
try { sl.Enter(ref taken); /* very short work */ } finally { if (taken) sl.Exit(); }

SpinWait.SpinUntil(() => Volatile.Read(ref _ready), TimeSpan.FromSeconds(1));
```
