# Forbidden — Duplicate / ambiguous models, services, and helpers

Rule slug: `no-duplicate-or-ambiguous-models`.

There is exactly one way to do any given thing in the codebase. Before creating a new model, service, helper, mapper, validator, or extension, **the developer searches first** for something that already does the job and extends / overloads / adjusts that. Creating a second shape for the same concept — especially a second DTO, a second service interface, a second helper class — is forbidden.

## What it looks like

```csharp
// Banned — two DTOs that describe the same concept
// File: Application/Orders/PlaceOrderCommand.cs
public sealed record PlaceOrderCommand(Guid AccountId, IReadOnlyList<OrderLine> Lines, Money Total);

// File: Web/Models/OrderRequest.cs   ← created later by another developer
public sealed class OrderRequest
{
    public Guid CustomerId { get; init; }              // same as AccountId, different name
    public List<OrderLineRequest> Items { get; init; } // same as Lines, different shape
    public decimal TotalAmount { get; init; }          // same as Total.Amount, no currency
}

// Banned — two services that do the same thing
public interface IEmailSender { Task Send(EmailMessage m); }
public interface IMailService { Task Dispatch(MailMessage m); }   // duplicate
public interface INotificationGateway { Task Notify(string to, string subject, string body); } // duplicate

// Banned — multiple equivalent helpers for the same operation
public static class GuidHelpers
{
    public static Guid NewSortable() => Guid.CreateVersion7();
}
public static class IdFactory
{
    public static Guid New() => Guid.CreateVersion7();       // duplicate
}
public static class Identifiers
{
    public static Guid Generate() => Guid.CreateVersion7();  // duplicate
}

// Banned — second mapper that maps the same pair
public sealed class OrderMapper : IOrderMapper { /* Domain → DTO */ }
public static class OrderDtoExtensions
{
    public static OrderDto ToDto(this Order o) { ... }       // duplicates IOrderMapper.ToDto
}

// Banned — bypass instead of extend
// Existing: public sealed class CancelOrderHandler { Task<Result> Handle(CancelOrderCommand cmd); }
// Wrong addition: a brand-new CancelOrderOrchestrator that wraps the same handler with a
// different method name because "extending the existing one felt risky".
```

## Why it's banned

1. **Ambiguity is a tax on every reader.** Two DTOs with overlapping fields force every reviewer to decide "which one is the right one?" — and the answer changes per call site.
2. **Duplication drifts.** Two services that "do the same thing" diverge the first time someone fixes a bug in one and not the other. The cost shows up six months later as inconsistent behaviour between two code paths.
3. **The glossary is the contract.** Every domain term has exactly ONE definition in `docs/GLOSSARY.md` and exactly ONE code identifier in `docs/DATA-MODEL.md`. A second DTO with a different name for the same concept violates the documented vocabulary before the code review even starts.
4. **It signals avoided refactoring.** "I don't want to touch the existing helper" is rarely a technical reason — it is usually a process / risk concern. The right answer is to refactor the existing helper safely (with the test suite as the safety net), not to bypass it.

## What to do instead — the search-before-create discipline

Before writing a new type, method, mapper, or helper, the developer (and the reviewer when checking):

1. **Search by the domain term.** Grep `docs/GLOSSARY.md` for the concept; the glossary lists the canonical **Code identifier**. If the identifier already exists, that is the one to extend.
2. **Search by candidate names.** Grep the codebase for the proposed type name AND for its likely synonyms (`Email`, `Mail`, `Notification`, `Message`). If anything in the same conceptual neighbourhood exists, treat it as the candidate to extend.
3. **Read what you found.** Open the existing type, list what it supports, identify the gap, decide between:
   - **Extend** — add a method / property / overload to the existing shape.
   - **Adjust** — change the existing shape's signature (with the test suite confirming nothing broke).
   - **Specialise** — derive a more specific subtype only when polymorphism genuinely earns its keep (rare; the team defaults to composition).
4. **Create only if step 3 fails.** "Failure" means: the concept genuinely does not exist (no glossary entry, no nearby identifier), or the existing shape models a different domain concept that happens to share a name. In both cases, the analyst is asked to add / clarify the glossary entry FIRST, then the new type lands with that name and a fresh code identifier.

The discipline applies to **every kind** of artifact: DTOs, command/result records, service interfaces, mappers, validators, extension methods, helpers, even private static utilities.

## Canonical first-party shapes (re-use these)

The team has first-party canonical shapes for the most common cross-cutting concerns. Use them instead of inventing variations.

| Concept | Canonical shape | Skill |
|---|---|---|
| Command / Query carrying a `CommandId` | hexagonal `Command` base | `dotnet-hexagonal-architecture` § core-and-infrastructure |
| Operation outcome with success / failure | hexagonal `Result` (`SuccessResult` / `FailedResult`) + app-wide `ErrorCode` enum | `dotnet-hexagonal-architecture` § core-and-infrastructure |
| Domain event raised from application | hexagonal `Event` base + delegate-first event broker | `dotnet-hexagonal-architecture` § core-and-infrastructure |
| Mapping domain ↔ DTO | hand-written `IXxxMapper` service | [no-automapper-no-mediatr.md](no-automapper-no-mediatr.md) |
| Stable identifier creation | `Guid.CreateVersion7()` (no `Guid.NewGuid()`) | [../csharp-style/guid-createversion7.md](../csharp-style/guid-createversion7.md) |
| Current time | injected `TimeProvider` (no `DateTime.UtcNow`) | [../csharp-style/time-provider.md](../csharp-style/time-provider.md) |
| Structured logging | `[LoggerMessage]` partials | [../source-generators/index.md](../source-generators/index.md) |
| JSON serialisation | `JsonSerializerContext` (no per-type `JsonSerializerOptions` allocations) | [../source-generators/index.md](../source-generators/index.md) |

If a proposed new type would shadow any of these, do not create it.

## Enforcement

- **Developer (`dotnet-developer`)** runs the search-before-create discipline BEFORE every new file. The handback report names the search done and the decision (extend / adjust / specialise / create).
- **Reviewer (`dotnet-reviewer`)** confirms the search was done by independently grepping the codebase for the new type's likely synonyms. A finding is written to `debt.md` with severity:
  - `blocker` — a brand-new DTO duplicates the canonical command/result shape verbatim (`Result` re-implemented under a different name).
  - `major` — a brand-new service interface overlaps an existing one with > 80 % method coverage.
  - `minor` — duplicated helper / utility / extension; cleared by collapsing into the first existing one.
- **Clean-as-you-touch:** while editing a file that uses a duplicate, switch the call site to the canonical shape in the same change (per [../build-quality/clean-as-you-touch.md](../build-quality/clean-as-you-touch.md)).

## Companion docs

- [../../development-documentation/glossary.md](../../development-documentation/glossary.md) — the single source of truth for the domain vocabulary.
- [../../development-documentation/data-model.md](../../development-documentation/data-model.md) — the entity / value-object / enum catalogue.
- `dotnet-hexagonal-architecture` — the canonical layout that already defines the shapes for `Command`, `Result`, `Event`, mappers.

## See also

- [../build-quality/clean-as-you-touch.md](../build-quality/clean-as-you-touch.md) — scope-bounded eradication policy.
- [no-automapper-no-mediatr.md](no-automapper-no-mediatr.md) — the first-party mapper rule, which exists for the same reason as this one (no two ways to do mapping).
