# `Guid.CreateVersion7()` for new IDs

## Rule

Generate every new identifier with `Guid.CreateVersion7()`. Never `Guid.NewGuid()`, never `new Guid()`, never a sequential-counter scheme. Applies to entity IDs, message IDs, idempotency keys, correlation IDs, audit row PKs.

## Rationale

- **Time-ordered.** UUID v7 embeds a Unix-millisecond timestamp prefix, so values sort chronologically.
- **B-tree friendly.** Time-ordered IDs cluster monotonically, eliminating the random-insert page-split storm that plagues SQL Server / Postgres / Cosmos with v4 GUIDs.
- **Globally unique.** Same uniqueness guarantees as v4 — the random suffix has 74 bits of entropy.
- **Standardized.** RFC 9562. Supported natively in .NET 9+; no external library needed.
- Backwards compatibility with `Guid` everywhere — same 16-byte layout, same APIs, drop-in replacement.

## Canonical shape

```csharp
public sealed class Order
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid CustomerId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    // ...
}

// In a controller / service
var order = new Order
{
    Id = Guid.CreateVersion7(),
    CustomerId = dto.CustomerId,
    CreatedAt = time.GetUtcNow(),
};
```

## When `Guid.NewGuid()` is OK

Only when the ID is ephemeral and never persisted, never indexed, and never sorted. In practice this is rare — virtually every GUID in the team's codebase is a database key. Default to `CreateVersion7()` and only fall back if there's a concrete reason.

## Enforcement

- **Banned constructor / factory:** `Guid.NewGuid()` and `new Guid()` (without args) for new ID generation. See [../forbidden-patterns/no-non-v7-guids.md](../forbidden-patterns/no-non-v7-guids.md).
- **Clean-as-you-touch:** swap in the same pass when the file is already open.
- **Code review:** flag any non-v7 GUID assignment to a persisted column.
