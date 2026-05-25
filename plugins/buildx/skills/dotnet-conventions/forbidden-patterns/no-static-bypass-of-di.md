# Forbidden — Static / manual bypass of the DI motor

Rule slug: `no-static-bypass-of-di`.

The project uses the standard .NET dependency-injection container (`IServiceCollection` + `IServiceProvider`). Every collaborator, configuration accessor, time source, logger, HTTP client, queue client, and DbContext is **constructor-injected**. Patterns that fetch a dependency outside the constructor — static singletons, service locator, manual `new`, `Activator.CreateInstance` for things the container already knows about — are forbidden unless an exception is explicitly documented in code.

## What it looks like

```csharp
// Banned — static singleton holding mutable / I/O state
public static class EmailSender
{
    public static SmtpClient Client { get; } = new SmtpClient("smtp.acme.com");

    public static Task Send(string to, string subject, string body) =>
        Client.SendMailAsync(new MailMessage("noreply@acme.com", to, subject, body));
}

public sealed class CancelOrderHandler
{
    public async Task<Result> Handle(CancelOrderCommand cmd)
    {
        // ...
        await EmailSender.Send(cmd.CustomerEmail, "Order cancelled", "...");
        // ...
    }
}

// Banned — service locator inside the handler body
public sealed class CancelOrderHandler(IServiceProvider sp)
{
    public async Task<Result> Handle(CancelOrderCommand cmd)
    {
        var repo = sp.GetRequiredService<IOrderRepository>(); // service locator
        var notif = sp.GetService<INotificationService>();    // anti-pattern
        // ...
    }
}

// Banned — manual `new` of a class the container already registers
public sealed class CancelOrderHandler
{
    public async Task<Result> Handle(CancelOrderCommand cmd)
    {
        var repo = new SqlOrderRepository(new SqlConnection("..."));
        // ...
    }
}

// Banned — static factory that hands out concrete singletons
public static class TimeProviderFactory
{
    private static readonly TimeProvider _instance = TimeProvider.System;
    public static TimeProvider Current => _instance;
}

// Banned — Activator on a container-known type
var sender = (IEmailSender)Activator.CreateInstance(typeof(SmtpEmailSender))!;
```

## Why it's banned

1. **Hidden dependencies.** A class that calls `EmailSender.Send(...)` does not declare the dependency in its constructor — the wiring is invisible at the call site, the lifetime is whatever the static field happens to be, and the test surface cannot replace it without statefulness leaks.
2. **Lifecycle drift.** Static / manual `new` ignores `Scoped` / `Transient` semantics. A handler that `new`-s its own `DbContext` opens a connection per call and never participates in the request scope; the container's `Scoped` registration is silently bypassed.
3. **Configuration drift.** Static singletons cannot read configuration injected by the host (`IConfiguration`, `IOptions<T>`) without further static bridges. The escape hatch always grows; the original static becomes a god-object.
4. **Lifetime mistakes.** Service locator (`IServiceProvider.GetService(...)`) inside a request-handler body resolves from whatever scope the locator was captured in — almost always wrong (resolves a singleton-scoped service that should be scoped, or vice versa).
5. **Untestable in integration tests.** The team's tests exercise the same code that runs in production through the Aspire AppHost; a static singleton cannot be reconfigured between test classes without `[AssemblyInitialize]` hacks (banned by `dotnet-testing`).

## What to do instead

```csharp
// Good — interface + DI registration + constructor injection
public interface IEmailSender
{
    Task Send(EmailMessage message, CancellationToken cancellationToken = default);
}

public sealed class SmtpEmailSender(SmtpClient smtp, IOptions<SmtpOptions> options) : IEmailSender
{
    public Task Send(EmailMessage message, CancellationToken cancellationToken = default) =>
        smtp.SendMailAsync(message.ToMailMessage(options.Value.From), cancellationToken);
}

// Composition root (Program.cs)
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection("Smtp"));
builder.Services.AddSingleton<SmtpClient>(sp =>
{
    var options = sp.GetRequiredService<IOptions<SmtpOptions>>().Value;
    return new SmtpClient(options.Host, options.Port);
});
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();

// Consumer — declares everything it needs in the constructor
public sealed class CancelOrderHandler(
    IOrderRepository repository,
    IEmailSender emailSender,
    TimeProvider time) : ICommandHandler<CancelOrderCommand>
{
    public async Task<Result> Handle(CancelOrderCommand command)
    {
        // ...
        await emailSender.Send(EmailMessage.OrderCancelled(...), default);
        // ...
    }
}
```

## The aptness rule

Slice-scope vs project-scope:

- **Project HAS a DI motor wired** (any non-trivial ASP.NET Core host, Aspire AppHost, generic Host worker, Blazor app): the rule applies in full. Any new static / manual `new` / service-locator pattern is a `major` slice-scope violation. Existing offenders the slice touches are cleaned in the same pass (clean-as-you-touch).
- **Project HAS NO DI motor wired** (rare — small file-based scripts, legacy frameworks): the rule still flags any *new* static you add, but the reviewer does NOT request a project-wide migration to DI. A single `structural` row in `debt.md` captures "project lacks DI motor end-to-end; not slated for migration" (severity `structural`, status `accepted`, owner `(no one)`). Slice-level rows for new violations co-exist with the structural row.

See `development-documentation` § debt § "The aptness rule" for the full reasoning and concrete row shapes.

## Allowed exceptions (must be documented)

A `static` is allowed when it is **pure and stateless** (no I/O, no DI-relevant state):

- Extension method classes (`public static class StringExtensions`).
- `static readonly` constants and immutable lookup maps.
- Source-generated `[LoggerMessage]` partial methods.
- `JsonSerializerContext`-derived classes.

When a non-pure static is genuinely unavoidable (interop with a third-party SDK that hard-requires a static initializer; bootstrap-time `AppContext.SetSwitch` calls; .NET hosting primitives), add a one-line C# comment immediately above the offender:

```csharp
// EXCEPTION-DI-BYPASS: vendor SDK requires a static initializer; called once from Program.cs.
LegacyVendor.GlobalConfig.Set(...);
```

The reviewer recognises this exact marker and skips the offender in future passes. Markers without a clear reason (`// EXCEPTION-DI-BYPASS: legacy`) are rejected — name the constraint, not the era.

## Enforcement

- **Reviewer (`dotnet-reviewer`)** greps every changed file for:
  - `static class` with non-extension public surface (and the surface returns concrete types or holds I/O state).
  - `IServiceProvider.GetService` / `GetRequiredService` calls outside `Program.cs` / composition-root files.
  - `new` of types that have an `[Inject]`-friendly constructor and are registered in `IServiceCollection`.
  - `Activator.CreateInstance<T>()` where `T` is a container-known type.
- Findings are written to `debt.md` per § "The aptness rule" — slice-scope row for the new violation, plus the project-scope `structural` row if the project is non-apt.
- **Clean-as-you-touch:** inside a file you are editing, replace static / locator / manual-`new` callsites with constructor injection in the same change (per [../build-quality/clean-as-you-touch.md](../build-quality/clean-as-you-touch.md)).

## See also

- `dotnet-hexagonal-architecture` § core-and-infrastructure — the canonical DI registration shape per layer.
- [no-mocks-in-consumer-di.md](no-mocks-in-consumer-di.md) — the testing flip side: no environment-forked registrations.
- [../build-quality/clean-as-you-touch.md](../build-quality/clean-as-you-touch.md) — scope-bounded eradication policy.
- `development-documentation` § debt — `debt.md` shape, severity, status discriminator, aptness rule.
