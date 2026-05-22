# Interlocked, Memory Model, Thread-Local State, and Async-Friendly Sync

## `Interlocked`

Lock-prefix CPU atomics. Methods do not throw.

| Op | Notes |
|---|---|
| `Increment(ref T)` / `Decrement(ref T)` | `int`, `long`, `uint`, `ulong`. |
| `Add(ref T, T)` | `int`, `long`, `uint`, `ulong`. |
| `Exchange(ref T, T) → T` | numerics, `IntPtr`, `object`, generic `<T>` (reference). |
| `CompareExchange(ref T, value, comparand) → original` | numerics, `IntPtr`, `object`, generic `<T>`. |
| `Read(ref long) → long` | atomic 64-bit read on 32-bit platforms. |
| `And(ref T, T)`, `Or(ref T, T)` | bitwise atomic. |
| `MemoryBarrier()` | full fence. |
| `MemoryBarrierProcessWide()` | OS-level cross-CPU fence. |
| `SpeculationBarrier()` | block speculative execution past this point. |

```csharp
static long _counter;
Interlocked.Increment(ref _counter);

// Lazy single-init via CAS
static MyService? _svc;
public static MyService Service =>
    _svc ?? Interlocked.CompareExchange(ref _svc, new MyService(), null) ?? _svc!;

// CAS retry loop for non-trivial atomic update
int spin;
do { spin = _state; }
while (Interlocked.CompareExchange(ref _state, Transform(spin), spin) != spin);
```

`Volatile.Read` / `Volatile.Write` provide acquire/release semantics on a single field without a full fence.

## Memory model / `volatile`

.NET 10 follows the ECMA CLI memory model strengthened on x86/x64 (essentially TSO). Rules of thumb:

- Use `Interlocked.*` or `lock`/`Lock` for shared mutable state — **default answer**.
- `volatile` keyword imposes acquire-on-read / release-on-write on a single field; **does NOT** replace `Interlocked` for read-modify-write. Most non-experts should not use it; prefer `Volatile.Read/Write` or `Interlocked`.
- ARM64 / weaker architectures need explicit barriers (`Interlocked.MemoryBarrier`) when reasoning about ordering between independent reads/writes.
- Reads/writes of `int`, `long` (on 64-bit), and reference types are atomic at the CLR level; that is **not** the same as ordered.

## Thread-local storage

| Mechanism | Granularity | Lazy init | Notes |
|---|---|---|---|
| `[ThreadStatic] static T _x;` | Per thread + AppDomain | No — each new thread sees `default(T)`; field initializer runs only on the **first** thread. | Best perf. Lazily initialize on first use. |
| `ThreadLocal<T>` | Per thread | Yes via `valueFactory` | `Value`, `IsValueCreated`, `Values` (when `trackAllValues: true`). `IDisposable`. |
| `Thread.AllocateDataSlot()` / `GetData/SetData` | Per thread | No | Stored as `object`; legacy. |
| `AsyncLocal<T>` | Per **async flow** (logical call context) | n/a | Propagates through `await` and `Task.Run`. Use for ambient context (e.g. correlation IDs). |

```csharp
private static readonly ThreadLocal<Random> _rng =
    new(() => new Random(Environment.TickCount + Environment.CurrentManagedThreadId));

private static readonly AsyncLocal<string> _correlationId = new();
_correlationId.Value = Guid.NewGuid().ToString();
await Task.Run(() => Console.WriteLine(_correlationId.Value));   // flows through await
```

## Async-friendly synchronization patterns

| Bad | Good |
|---|---|
| `Thread.Sleep(t)` | `await Task.Delay(t, ct)` |
| `task.Wait()` / `task.Result` in async path | `await task` |
| `ManualResetEvent.WaitOne()` (in async) | `TaskCompletionSource<T>` + `await tcs.Task` |
| `lock { await ... }` (compile error) | `SemaphoreSlim(1, 1).WaitAsync()` |
| `Monitor.Wait` / `Pulse` for queues | `Channel<T>` |

Async mutex:

```csharp
private readonly SemaphoreSlim _lock = new(1, 1);

public async Task<T> WithLockAsync(Func<Task<T>> work, CancellationToken ct)
{
    await _lock.WaitAsync(ct);
    try { return await work(); }
    finally { _lock.Release(); }
}
```
