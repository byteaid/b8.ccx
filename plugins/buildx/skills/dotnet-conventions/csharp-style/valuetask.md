# `ValueTask` where allocation matters

## Rule

Return `ValueTask` / `ValueTask<T>` for hot-path async methods that frequently complete synchronously — cache hits, fast-path validators, pooled-resource accessors, low-allocation library APIs. Default to `Task` / `Task<T>` everywhere else.

## Rationale

- `ValueTask` avoids allocating a `Task` heap object when the operation completes synchronously.
- High-throughput pipelines (per-request hot loops, telemetry emitters, EF Core change tracking) accumulate measurable overhead from millions of `Task` allocations.
- `ValueTask` has stricter usage rules than `Task` — it must be awaited at most once and not stored. Use it deliberately.

## Canonical shape

```csharp
public sealed class CachedUserStore(IMemoryCache cache, IUserSource source) : IUserStore
{
    public ValueTask<User?> GetAsync(Guid id, CancellationToken ct = default)
    {
        if (cache.TryGetValue<User>(id, out var hit))
            return new ValueTask<User?>(hit);    // synchronous fast path — no allocation

        return new ValueTask<User?>(LoadAsync(id, ct));
    }

    private async Task<User?> LoadAsync(Guid id, CancellationToken ct)
    {
        var user = await source.LoadAsync(id, ct);
        cache.Set(id, user, TimeSpan.FromMinutes(5));
        return user;
    }
}
```

## Rules of use

- Await **at most once**. `ValueTask` may wrap a pooled `IValueTaskSource`; multiple awaits are undefined.
- Do **not** store in a field, capture in a closure, or pass through `Task.WhenAll`. Convert to `Task` first via `.AsTask()` if you need those.
- Public library APIs that are not on a hot path: stick with `Task` — simpler contract.

## When NOT to use

- Method that always awaits I/O — `Task` is fine, no synchronous fast path to optimize.
- Public API consumed by code that may inadvertently violate the await-once rule.

## Enforcement

- **Code review:** flag `ValueTask` returns that always perform real async work (no fast path) — revert to `Task`.
- **Code review:** flag any `ValueTask` field, multiple-await, or pass to `Task.WhenAll` — convert to `Task`.
