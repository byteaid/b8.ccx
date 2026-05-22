# Forbidden — `Guid.NewGuid()` and non-v7 GUIDs for new IDs

## What it looks like

```csharp
public sealed class Order
{
    public Guid Id { get; init; } = Guid.NewGuid();        // banned — v4 GUID
}

var id = Guid.NewGuid();
order.CorrelationId = Guid.NewGuid();

var fromString = new Guid("12345678-90ab-cdef-1234-567890abcdef");   // arbitrary v4
var sequential = SequentialGuidGenerator.Next();                      // banned — bespoke scheme
```

## Why it's banned

1. **v4 GUIDs are random.** Inserting them into a B-tree-indexed PK column produces page splits on every insert, fragmenting the index and tanking write throughput.
2. **v4 GUIDs are not sortable.** "What was the last order?" requires a `CreatedAt` column — an extra index, an extra read, an extra surface for time-skew bugs.
3. **`Guid.CreateVersion7()` (.NET 9+) is the standard answer.** RFC 9562. Time-ordered prefix + 74 random bits. B-tree-friendly, sortable, globally unique.
4. **Bespoke sequential schemes** (`SequentialGuidGenerator`, `Guid.NewGuid()` with prefix manipulation) reinvent v7 badly — they lack standardization and analytics tooling support.

## What to do instead

```csharp
public sealed class Order
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid CorrelationId { get; init; } = Guid.CreateVersion7();
}

// Anywhere
var id = Guid.CreateVersion7();
```

`Guid.CreateVersion7()` produces a `Guid` value indistinguishable in API surface from any other GUID — same 16 bytes, same `ToString()`, same equality, same JSON serialization. The difference is purely in the byte layout, which the database engine exploits for B-tree clustering.

## When `Guid.NewGuid()` is acceptable

The bar is high. Use it only when **all** of the following hold:

- The value is ephemeral — never persisted, never indexed.
- The value is never sorted.
- Time-ordering is irrelevant to the use case.

In practice this is rare — most GUIDs in a real codebase end up in a database row. Default to `CreateVersion7()` and fall back only with a documented reason.

## Enforcement

- **On sight, inside a file you're editing:** swap `Guid.NewGuid()` for `Guid.CreateVersion7()`. See [../build-quality/clean-as-you-touch.md](../build-quality/clean-as-you-touch.md).
- **Quick scan:**

  ```bash
  grep -rE "Guid\.NewGuid\(\)" src/ \
    | grep -v "test/.*\.Test/"
  ```

  must return no matches in production code (tests may use either form for non-persisted assertions).

## See also

- [../csharp-style/guid-createversion7.md](../csharp-style/guid-createversion7.md) — the positive rule and canonical shape.
