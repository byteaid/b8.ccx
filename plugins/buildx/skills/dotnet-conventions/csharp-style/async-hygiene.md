# Async hygiene

## Rule

Async-all-or-none. `CancellationToken` propagates end-to-end. Never block on async code with `.Result`, `.Wait()`, `.GetAwaiter().GetResult()`, or `Task.Run(() => asyncMethod()).Result`. Suffix async methods with `Async`.

## Rationale

- `.Result` / `.Wait()` deadlock on synchronization-context-bound threads (UI, legacy `AspNetSynchronizationContext`) and waste threads in the modern thread pool.
- Async-over-sync (`Task.Run` to call sync code) and sync-over-async (`.Result` to call async code) hide problems instead of fixing them.
- A `CancellationToken` that does not propagate is worse than no token — the caller thinks cancellation works when it doesn't.
- `Async` suffix is a non-negotiable convention: it makes diff review and grep effective.

## Canonical shape

```csharp
public sealed class OrderService(AppDb db, IOrderRepository repo) : IOrderService
{
    // Async naming, token first-class, ConfigureAwait NOT needed in app code (no SyncContext).
    public async Task<Order> GetAsync(Guid id, CancellationToken ct)
    {
        var order = await repo.GetAsync(id, ct);                  // token flows
        if (order is null)
            throw new InvalidOperationException("Order missing."); // exceptional, not business

        await db.Audits.AddAsync(new Audit(...), ct);              // token flows
        await db.SaveChangesAsync(ct);                             // token flows
        return order;
    }
}
```

## Banned patterns

```csharp
// Sync-over-async — deadlocks under SyncContext, wastes threads otherwise.
var order = repo.GetAsync(id).Result;
var order = repo.GetAsync(id).GetAwaiter().GetResult();
repo.SaveAsync().Wait();

// Token swallowed — the cancellation your caller sent never reaches the work.
public Task<Order> GetAsync(Guid id, CancellationToken ct) => repo.GetAsync(id);   // ct unused

// Async void — uncatchable exceptions, untestable, except for event handlers.
public async void HandleSomething() { ... }
```

## Rules in detail

- **Token first-class.** Every async method that calls into anything else takes `CancellationToken ct` as the last parameter (after defaults) and passes it through.
- **No `ConfigureAwait(false)` in application code.** ASP.NET Core has no `SynchronizationContext` since .NET Core 3; it adds noise. Library code targeting platforms with sync contexts is the carve-out.
- **No `async void`** except for actual event handlers (`EventHandler` signature).
- **No `Task.Run` in async methods** to "make it async" — it just shifts work to another pool thread.

## Enforcement

- **Code review:** any `.Result` / `.Wait()` / `.GetAwaiter().GetResult()` is a blocking finding.
- **Code review:** flag missing `Async` suffix on `Task`-returning methods.
- **Clean-as-you-touch:** convert in the same pass when the file is already open and the call sites are local.
