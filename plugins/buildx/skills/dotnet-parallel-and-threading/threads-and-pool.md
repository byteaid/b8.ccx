# Threads and Thread Pool

## Threads — `System.Threading.Thread`

Use for: foreground workers, threads with specific priority / stack size / apartment, dedicated long-running blocking work. Otherwise use `Task` / `ThreadPool`.

```csharp
var t = new Thread(static obj => Worker((string)obj!))
{
    Name = "worker-1",
    IsBackground = true,
    Priority = ThreadPriority.Normal
};
t.Start("payload");
t.Join();
```

- **Foreground** — keeps process alive until thread exits.
- **Background** — process exits without waiting. All `ThreadPool` / TPL threads are background.
- `Thread.Abort()` → `PlatformNotSupportedException` on .NET 5+ (SYSLIB0006). Replace with cancellation.

## Managed thread pool — `ThreadPool`

```csharp
ThreadPool.QueueUserWorkItem(state => Work(state), state: payload);
ThreadPool.QueueUserWorkItem<MyState>(static (s) => Work(s), state, preferLocal: true);
ThreadPool.UnsafeQueueUserWorkItem(workItem, preferLocal: false);   // skips ExecutionContext capture

// Custom work item — no allocation per queue
sealed class MyItem : IThreadPoolWorkItem
{
    public void Execute() { /* ... */ }
}
ThreadPool.UnsafeQueueUserWorkItem(new MyItem(), preferLocal: true);

ThreadPool.GetMinThreads(out int worker, out int io);
ThreadPool.SetMinThreads(64, 64);   // raise floor for burst-y servers
long pending = ThreadPool.PendingWorkItemCount;
```

Default min ≥ `ProcessorCount`. Above the min, the pool injects/retires threads via a hill-climbing throughput heuristic — typically one new thread per ~500 ms when starved. Raising `SetMinThreads` reduces ramp latency for thundering-herd patterns; setting it too high masks contention bugs and wastes memory.

Don't use the pool when you need a foreground thread, a specific priority, long blocking work, an STA, or a stable thread identity. Unhandled exceptions on a pool thread terminate the process.
