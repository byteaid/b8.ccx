# `.Interface` Project, Shared Bases, and `ErrorCode`

`Company.Product.Interface` is the **Host ↔ Application** contract surface. It carries data only — Commands, Results, Events, Completions — never service interfaces and never infrastructure abstractions.

## Folder structure

```
Company.Product.Interface/
├── Commands/
│   ├── Command.cs                  # shared base
│   ├── ProductCommands.cs
│   └── OrderCommands.cs
├── Results/
│   ├── Result.cs                   # shared base
│   ├── ProductResults.cs
│   └── OrderResults.cs
├── Events/
│   ├── Event.cs                    # shared base
│   ├── ProductEvents.cs
│   └── OrderEvents.cs
└── Completions/
    └── OperationCompletions.cs
```

One file per aggregate per category (e.g. `ProductCommands.cs` holds `CreateProductCommand`, `UpdateProductCommand`, …). The base lives in its own file at the root of each folder.

## Shared bases

Every concrete `*Command`, `*Result`, and `*Event` derives from a corresponding base. The bases are bare-named — `Command`, `Result`, `Event` — never `BaseCommand`/`BaseResult`/`BaseEvent`. They carry the cross-cutting fields (correlation id, timestamps); concrete types add only their own payload.

```csharp
// Company.Product.Interface/Commands/Command.cs
public abstract class Command
{
    public Guid CommandId { get; init; } = Guid.NewGuid();
    public DateTime IssuedAt { get; init; } = DateTime.UtcNow;
}

// Company.Product.Interface/Results/Result.cs
public abstract class Result
{
    public Guid CommandId { get; init; }
    public DateTime At { get; init; } = DateTime.UtcNow;
}

// Company.Product.Interface/Events/Event.cs
public abstract class Event
{
    public Guid CommandId { get; init; }
    public DateTime At { get; init; } = DateTime.UtcNow;
}
```

Result subtypes split on the outcome:

```csharp
public class ProductCreatedCompletion : Result
{
    public Guid ProductId { get; init; }
}

public class FailedResult : Result
{
    public ErrorCode Code { get; init; }
    public string? Detail { get; init; }       // optional non-localized debug detail (logs, not UI)
}
```

`Completions/` holds **terminal** result types — the success-shape responses an operation returns when it completes. Use `*Completion` as the suffix when the type represents the happy-path payload (`ProductCreatedCompletion`, `OrderShippedCompletion`); use `FailedResult` for failures and lean on `ErrorCode` to distinguish reasons.

## What does NOT live in `.Interface`

| Type | Lives in |
|---|---|
| `IProductService`, `IOrderService` (service interfaces) | Application project (`Company.Product`). |
| `IRepository`, `ICache`, `IStorage`, `IMessageBus` (infrastructure abstractions) | `Company.Product.Infrastructure`. |
| `Product`, `Order`, `OrderItem` (domain entities, value objects) | `Company.Product.Models`. |
| `ProductStatus`, `ErrorCode`, business constants | `Company.Product.Constants`. |
| Mapper interfaces (`IProductMapper`) | The adapter project that needs the mapper (`Company.Product.SqlServer`). |

If a type starts with `I`, it does not belong in `.Interface`.

## `ErrorCode` — single app-wide failure taxonomy

`ErrorCode` lives in `Company.Product.Constants` and is the **shared vocabulary** between backend and frontend. It is one enum for the whole application — never split per module, never replaced with free-form `string Message` fields. The application returns a `Result` carrying an `ErrorCode`; the frontend translates the code into a localized, contextualized, actionable user-facing message.

### Rules

- `0` is reserved for `Unknown` and indicates a bug. Never return it deliberately.
- Numeric ranges group entries by category. Gaps within a range are intentional — categories grow without renumbering.
- Once a code ships to a client it cannot be removed or renumbered. Deprecate by leaving it in place and adding a successor; a comment marking it deprecated is acceptable.
- New failure modes are added here, never in a sibling enum.

### Canonical taxonomy

```csharp
public enum ErrorCode
{
    Unknown = 0,

    // 1xxx — Input validation
    ValidationFailed       = 1000,
    RequiredFieldMissing   = 1001,
    InvalidFormat          = 1002,
    OutOfRange             = 1003,
    PayloadTooLarge        = 1004,

    // 2xxx — Authentication / authorization
    Unauthenticated        = 2000,
    Unauthorized           = 2001,
    TokenExpired           = 2002,
    TokenInvalid           = 2003,
    SessionRevoked         = 2004,

    // 3xxx — Business rules
    BusinessRuleViolated   = 3000,
    PreconditionFailed     = 3001,
    InvalidStateTransition = 3002,
    PolicyDenied           = 3003,

    // 4xxx — Resource state
    NotFound               = 4000,
    AlreadyExists          = 4001,
    Conflict               = 4002,
    Gone                   = 4003,
    Locked                 = 4004,

    // 5xxx — Infrastructure availability
    InfrastructureUnavailable = 5000,
    DatabaseUnavailable       = 5001,
    CacheUnavailable          = 5002,
    StorageUnavailable        = 5003,
    MessagingUnavailable      = 5004,
    Timeout                   = 5005,

    // 6xxx — Quotas / rate limits
    RateLimited            = 6000,
    QuotaExceeded          = 6001,
    ConcurrencyLimitHit    = 6002,

    // 7xxx — External dependencies
    ExternalDependencyFailed = 7000,
    PaymentProviderRejected  = 7001,
    EmailProviderRejected    = 7002,

    // 9xxx — Catch-all
    InvalidOperation       = 9000,
    NotImplemented         = 9001
}
```

### Frontend contract

- Translate `ErrorCode` into a user-facing message (i18n catalog keyed by the enum).
- Decide UI affordances per category: retry on `5xxx`, "go back" on `4000 NotFound`, inline form error on `1xxx`, upgrade prompt on `6001 QuotaExceeded`.
- Never render `Detail` to the user; it is for support / debugging only.

### Adding a new code

1. Pick the right range (`1xxx`–`9xxx`).
2. Pick a name that names the **failure**, not the **call** (`PreconditionFailed`, not `OrderServicePreconditionFailed`).
3. Insert in numeric order within the range. If the next round number is taken, jump by 1 (e.g. `4005`).
4. Update the frontend i18n catalog with a translation entry.
5. Use it from `FailedResult.Code` at the application service that needs it.

## Cross-references

- [solution-layout.md](solution-layout.md) — where `.Interface` and `.Constants` sit physically.
- [core-and-infrastructure.md](core-and-infrastructure.md) § Events — events declared here are raised in the application services.
- [dependency-flow.md](dependency-flow.md) — Interface depends on `.Models` + `.Constants`; nothing else may reference Interface besides Host and Application.
