# Forbidden — Leaking exception detail to the user, and unlogged exceptions

Rule slug: `exceptions-logged-not-leaked`. **Priority: `P0` — non-negotiable and non-deferrable** (see `development-documentation` § debt § Priority). A `P0` finding is never carried as debt, never `accepted`, never `slated`: it is cleared in the same slice that introduced it, or immediately on discovery.

Two paired sub-rules:

1. **Never surface raw exception detail to the user.** `ex.Message`, `ex.ToString()`, stack traces, inner-exception text, SQL / vendor error strings must never reach a UI surface, an HTTP response body, an API error field, or any user-visible channel. The user sees a **generic, English, named message** (a constant from `ErrorMessages`) — optionally accompanied by a correlation id.
2. **Every caught exception is logged via `ILogger`.** The detail the user does not see is captured through `ILogger` (preferably a `LoggerMessage` source-generated method) with enough context — ids, correlation — to diagnose. A `catch` that hides the failure from the user but never logs it is just as forbidden as one that leaks it.

## What it looks like

```csharp
// Banned — leaks ex.Message to the UI AND never logs
catch (Exception ex)
{
    _wizardError = $"Error creating identity: {ex.Message}";
    _wizardStep  = 1;
}

// Banned — leaks exception text into an HTTP response
return Problem(detail: ex.ToString());
return BadRequest(ex.Message);

// Banned — generic message shown, but the exception is swallowed unlogged
catch (Exception)
{
    return ErrorMessages.CreateIdentity; // operations never learn this failed
}
```

## Why it's banned

1. **Security.** Exception detail leaks internals — stack frames, type and assembly names, SQL fragments, connection info, file paths — that are reconnaissance material for an attacker; it can also leak PII embedded in the message.
2. **UX.** Raw exception text is unintelligible to users and frequently non-English; it is never an acceptable end-user message.
3. **Diagnosability.** A failure that is hidden from the user but never logged is invisible to operations — you cannot fix, alert on, or even count what you never recorded.
4. **Together they are the line between a safe failure and an incident.** That is why the team rates the pair `P0`: not negotiable, not deferrable.

## What to do instead

```csharp
catch (Exception ex)
{
    LogCreateIdentityFailed(logger, apimApiId, ex);   // full detail to ILogger
    _wizardError = ErrorMessages.CreateIdentity;       // generic, named, English
    _wizardStep  = 1;
}

[LoggerMessage(Level = LogLevel.Error, Message = "Failed to create identity for API {ApimApiId}")]
private static partial void LogCreateIdentityFailed(ILogger logger, string apimApiId, Exception ex);
```

At an application-layer boundary, the global handler logs once and returns a typed failure — never the exception text:

```csharp
catch (Exception ex)
{
    LogUnhandled(logger, command.CommandId, ex);
    return new FailedResult { CommandId = command.CommandId, Code = ErrorCode.UnhandledException };
}
```

A **correlation id** (the command id, a trace id) may be surfaced to the user to bridge to the logged entry — that is allowed, because an opaque id is not exception detail. The generic message is a named constant (see [no-magic-literals.md](no-magic-literals.md)); the typed boundary shape is owned by [try-catch-must-do-work.md](try-catch-must-do-work.md).

## Priority — `P0` (non-negotiable, non-deferrable)

- **Cannot be `accepted`.** No one — not even the user — may authorise leaking exception detail or skipping the log. There is no carve-out.
- **Cannot be carried.** The orchestrator does NOT close a slice while a `P0` row produced by that slice is open; it re-dispatches the developer to clear it first.
- **No structural parking.** A pre-existing (legacy) instance found while editing is fixed in the same touch — it is not recorded as `structural` debt to defer. The only legacy tolerance is for *identifiers already in the codebase* (see english-only), never for leaking or swallowing exceptions.

## Enforcement

- **Reviewer (`dotnet-reviewer`):** any data path from a `catch` to a user-visible surface that carries `ex.Message` / `ex.ToString()` / `.StackTrace` / inner-exception text → one `P0` row. Any `catch` with no `ILogger` call and no rethrow to a logging boundary → one `P0` row. Both classified `P0`, status `active`, owner = developer; surfaced as a must-clear-before-closure finding.
- **Developer:** applies at write-time, and clean-as-you-touch even outside the current slice — a `P0` violation found in a file you are editing is cleared in the same change.
- **Quick scans:**

  ```bash
  grep -rnE "ex\.(Message|StackTrace)|\.ToString\(\)" src/   # candidate leaks — inspect each user-facing path
  grep -rnzoE "catch[^{]*\{[^}]*\}" src/                      # catch bodies — confirm an ILogger call is present
  ```

## See also

- [try-catch-must-do-work.md](try-catch-must-do-work.md) — the boundary global handler that logs once and returns `FailedResult { Code = ErrorCode.UnhandledException }`.
- [no-magic-literals.md](no-magic-literals.md) — `ErrorMessages` is the home for the generic message.
- [../csharp-style/english-only.md](../csharp-style/english-only.md) — the generic message and the log text are English.
- [../source-generators/loggermessage.md](../source-generators/loggermessage.md) — the `LoggerMessage` source-generated logging method.
- `development-documentation` § debt § Priority — the `P0` tier and its closure rules.
