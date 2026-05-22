# `IAsyncEnumerable<T>` for streaming results

## Rule

Return `IAsyncEnumerable<T>` (with `[EnumeratorCancellation] CancellationToken`) for any operation that yields more than a handful of items and where the caller benefits from streaming — repository scans, paginated queries, log readers, server-streaming gRPC, event projections. Materializing into `List<T>` for known-large results is a code smell.

## Rationale

- Constant memory regardless of result size; no buffering of the full set.
- Back-pressure is implicit — the consumer pulls at its own pace.
- Plays cleanly with EF Core 10 (`AsAsyncEnumerable()`), gRPC server streaming, and SignalR streaming.
- `await foreach` reads naturally and propagates cancellation.

## Canonical shape

```csharp
public sealed class OrderRepository(AppDb db) : IOrderRepository
{
    public async IAsyncEnumerable<Order> StreamRecentAsync(
        DateTimeOffset since,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var order in db.Orders
            .Where(o => o.CreatedAt >= since)
            .OrderBy(o => o.CreatedAt)
            .AsAsyncEnumerable()
            .WithCancellation(ct))
        {
            yield return order;
        }
    }
}

// Consumer
await foreach (var order in repo.StreamRecentAsync(cutoff, ct))
{
    await sink.WriteAsync(order, ct);
}
```

## When NOT to use

- Result is bounded and small (≤ ~100 items) and the caller wants `List<T>` for indexing.
- The operation is a single-row read.
- The downstream protocol cannot stream (e.g., classic JSON response that has to be a complete array — though `JsonSerializer.SerializeAsync` over `IAsyncEnumerable<T>` works in modern ASP.NET Core).

## Enforcement

- **Code review:** flag `Task<List<T>>` repository methods where the result set is unbounded; convert to `IAsyncEnumerable<T>`.
- **Always propagate the token:** missing `[EnumeratorCancellation]` is a bug — the consumer's cancellation will not flow into the iterator.
