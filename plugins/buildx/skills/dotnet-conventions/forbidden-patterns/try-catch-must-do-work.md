# Forbidden — Try/catch that does no real work (and missing app-layer global handler)

Rule slug: `try-catch-must-do-work`.

Two distinct sub-rules under one banner:

1. **No empty / log-only / nested-without-purpose try/catch.** A `catch` block that only logs (or only re-throws, or only swallows) is forbidden.
2. **Every entry point of the application layer MUST carry a single global try/catch** that captures uncontrolled exceptions and surfaces them safely and organically (typed `Result` / `ErrorCode` / problem-details response) — exactly once, at the boundary.

## What it looks like

### Sub-rule 1 — useless catches

```csharp
// Banned — catch does nothing useful
try
{
    var result = await _orderService.Cancel(command);
    return result;
}
catch (Exception ex)
{
    _logger.LogError(ex, "Cancel failed");
    throw; // re-throwing the same exception adds nothing — just remove the try/catch
}

// Banned — silent swallow
try
{
    await _outbox.Publish(evt);
}
catch
{
    // pretend it worked
}

// Banned — nested catches with no decision logic
try
{
    var a = Step1();
    try
    {
        var b = Step2(a);
        try
        {
            return Step3(b);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "step3");
            throw;
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "step2");
        throw;
    }
}
catch (Exception ex)
{
    _logger.LogError(ex, "step1");
    throw;
}
```

### Sub-rule 2 — missing global handler at the application entry point

```csharp
// Banned — application service exposes its entry method with no global safety net
public sealed class CancelOrderHandler(IOrderRepository repo, ILogger<CancelOrderHandler> log)
{
    public async Task<Result> Handle(CancelOrderCommand command)
    {
        // direct work — if an unexpected exception escapes, callers (HTTP / gRPC / queue
        // adapters) decide individually how to handle it, with no uniform shape.
        var order = await repo.GetById(command.OrderId);
        order.Cancel(_time.GetUtcNow());
        await repo.Save(order);
        return new SuccessResult { CommandId = command.CommandId };
    }
}
```

## Why it's banned

1. **A useless catch is noise that masks real handling.** Reviewers stop trusting catch blocks once half of them just re-throw with a log line. The honest shape is "no try/catch" — let the exception propagate to the boundary handler.
2. **Log-only catches violate the single-responsibility of error handling.** Logging is a side effect of handling, not handling itself. If the catch does not transform the failure into a typed `Result`, retry, compensate, or escalate, it is doing nothing.
3. **Nested catches without decision logic are a 100% smell** — they exist because someone "wrapped every step just in case". They should be exactly one catch at the boundary OR zero catches with the exception propagating to the boundary handler.
4. **A missing global handler at the application entry point is the actual gap a useless catch tries to paper over.** Every application service that maps a command to a `Result` must catch unexpected exceptions ONCE at the public method and produce a `FailedResult { Code = ErrorCode.UnhandledException }` (or the project's equivalent), with the exception detail captured into a structured log AND a stable correlation id surfaced back. That is what makes the failure "safe and organic" to the caller.

## What to do instead

### For useless catches — delete them

```csharp
// Good — no try/catch, the global handler at the boundary owns unexpected failures
public async Task<Result> Cancel(CancelOrderCommand command)
{
    var order = await _repository.GetById(command.OrderId);
    if (order is null)
        return new FailedResult { CommandId = command.CommandId, Code = ErrorCode.NotFound };

    if (order.Status == OrderStatus.Shipped)
        return new FailedResult { CommandId = command.CommandId, Code = ErrorCode.InvalidStateTransition };

    order.Cancel(_time.GetUtcNow());
    await _repository.Save(order);
    return new SuccessResult { CommandId = command.CommandId };
}
```

### For the boundary — exactly one global try/catch

```csharp
// Good — one global try/catch at the application-layer entry point
public sealed class CancelOrderHandler(
    IOrderRepository repository,
    TimeProvider time,
    ILogger<CancelOrderHandler> logger) : ICommandHandler<CancelOrderCommand>
{
    public async Task<Result> Handle(CancelOrderCommand command)
    {
        try
        {
            return await ExecuteCore(command);
        }
        catch (Exception ex)
        {
            LogUnhandled(logger, command.CommandId, ex);
            return new FailedResult
            {
                CommandId  = command.CommandId,
                Code       = ErrorCode.UnhandledException,
                Detail     = ex.GetBaseException().Message,
                Correlation= command.CommandId,
            };
        }
    }

    private async Task<Result> ExecuteCore(CancelOrderCommand command)
    {
        var order = await repository.GetById(command.OrderId);
        if (order is null)
            return new FailedResult { CommandId = command.CommandId, Code = ErrorCode.NotFound };

        if (order.Status == OrderStatus.Shipped)
            return new FailedResult { CommandId = command.CommandId, Code = ErrorCode.InvalidStateTransition };

        order.Cancel(time.GetUtcNow());
        await repository.Save(order);
        return new SuccessResult { CommandId = command.CommandId };
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception in CancelOrderHandler (CommandId={CommandId})")]
    private static partial void LogUnhandled(ILogger logger, Guid commandId, Exception ex);
}
```

### When a localised try/catch IS allowed

Localised catches are allowed only when the catch block makes a **decision** based on the exception type:

- **Compensation / fallback** — e.g., `catch (SqlException ex) when (ex.Number == 2601)` retries with a deduped payload.
- **External-resource retry policy** — Polly retry on transient HTTP failures, where the catch produces a different outcome than the throw.
- **Domain-specific exception → domain Result** — translate a vendor exception into the project's `ErrorCode` enum.

In every case, the catch must produce different observable behaviour than re-throwing. If the catch's only effect is `log + rethrow`, delete it.

## Enforcement

- **Reviewer (`dotnet-reviewer`)** scans every changed file for `catch` blocks. Each is classified:
  - decision-producing → OK
  - log-only / re-throw-only → flag (slug: `try-catch-must-do-work`, severity `minor` if isolated, `major` if widespread within the file).
  - nested without per-level decision → flag as a single `major` row covering the nest.
- **Reviewer (`dotnet-reviewer`)** scans every command/query handler under `Application/` (or the project's equivalent) for the boundary handler shape. A handler whose public method lacks the try/catch wrapper is flagged with severity `blocker` if the missing safety net leaks raw exceptions to the HTTP / gRPC / queue layer; `major` if the missing wrapper happens inside a handler that is currently only called from another internal handler.
- **Clean-as-you-touch:** while editing a file already containing useless catches, eradicate them in the same change.
- **Test-designer** never writes a test that depends on a specific exception escaping the handler — the test asserts the `Result` / HTTP shape produced by the boundary handler.

## See also

- [../build-quality/clean-as-you-touch.md](../build-quality/clean-as-you-touch.md) — scope-bounded eradication policy.
- [../csharp-style/index.md](../csharp-style/index.md) — `Result` base, `ErrorCode` enum (typed failure shape replaces exception-as-control-flow).
- `dotnet-hexagonal-architecture` § application-services — the application-layer entry point is where the global handler lives.
- `dotnet-events-exceptions` — exception-design conventions (when to throw, when to translate).
