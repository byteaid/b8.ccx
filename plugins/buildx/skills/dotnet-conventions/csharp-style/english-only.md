# English-only

## Rule

Every artifact written by the team is in English. No exceptions:

- Code: identifiers (types, methods, parameters, fields, locals), namespaces, project names, file names.
- Comments and XML doc.
- Log messages, exception messages, validation messages.
- Commit messages, branch names, PR titles and descriptions.
- `docs/*.md` (REQUIREMENT, FLOWS, ARCHITECTURE, ASSESSMENT, CHANGELOG, BACKLOG, BUGS, PROGRESS).
- Test names and `[TestProperty]` values.

User-facing UI text is the only carve-out and is handled by the i18n / resource pipeline, not by inlining non-English strings into source.

## Rationale

- The team and the agent fleet operate in English; mixed-language code raises the cost of every search, review, and grep.
- Tooling (analyzers, source generators, IDE refactorings, AI assistants) is calibrated against English identifiers.
- A single language across artifacts is the cheapest way to keep the codebase legible to every contributor.
- Translating later is more expensive than writing in English the first time.

## Canonical shape

```csharp
// Good
public sealed class OrderService(IOrderRepository repository) : IOrderService
{
    public async Task<Result> CancelOrder(CancelOrderCommand command)
    {
        var order = await repository.GetById(command.OrderId);
        if (order is null)
            return new FailedResult { CommandId = command.CommandId, Code = ErrorCode.NotFound };

        if (order.Status == OrderStatus.Shipped)
            return new FailedResult { CommandId = command.CommandId, Code = ErrorCode.InvalidStateTransition };

        // ...
    }
}
```

## Enforcement

- **Code review:** flag any non-English identifier or message; ask for a renaming.
- **Clean-as-you-touch:** rename inside the file you're editing when the rename is safe (private members, locals); for public APIs surface a TODO and report the cascade.
- **Commit hooks:** consider a CI check that rejects branch names / commit subjects with non-ASCII letters outside English-typical characters.
