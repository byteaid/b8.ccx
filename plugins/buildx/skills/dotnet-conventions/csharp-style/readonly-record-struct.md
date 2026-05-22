# `readonly record struct` for value objects

## Rule

Use `readonly record struct` for small immutable value types: identifiers, money, coordinates, ranges, enum-like value wrappers, DTOs that are passed by value frequently. Use `record` (class) when the type is referenced often or holds collections.

## Rationale

- Value semantics by structural equality — no manual `Equals`/`GetHashCode`.
- `readonly` modifier prevents defensive copies the compiler would otherwise emit.
- Allocation-free when stack-resident; ideal for hot paths.
- `with`-expressions give safe non-destructive mutation.
- Records compose well with primary constructors, deconstruction, and pattern matching.

## Canonical shape

```csharp
public readonly record struct OrderId(Guid Value)
{
    public static OrderId New() => new(Guid.CreateVersion7());
    public override string ToString() => Value.ToString();
}

public readonly record struct Money(decimal Amount, string Currency)
{
    public static Money Zero(string currency) => new(0m, currency);
    public Money Add(Money other) => Currency == other.Currency
        ? this with { Amount = Amount + other.Amount }
        : throw new InvalidOperationException("Currency mismatch.");
}

public readonly record struct DateRange(DateOnly From, DateOnly To);
```

## When `record` (class) instead

- Type holds collections (`record Order(IReadOnlyList<OrderLine> Lines)`).
- Type is large enough that copy-by-value is wasteful (rough rule: > 16 bytes).
- Type participates in EF Core / serialization scenarios that prefer reference semantics.

## When `class` instead

- Mutable entity with identity (EF Core entity, domain aggregate root).
- Behavior-heavy type with virtual hooks (rare — see [sealed-by-default.md](sealed-by-default.md)).

## Enforcement

- **Code review:** flag plain `class` value objects with manually written `Equals`/`GetHashCode`/`==` overloads — convert to `readonly record struct`.
- **Clean-as-you-touch:** swap in the same pass when the file is already open.
