# Forbidden — Magic literals in code (inline strings / numbers / status values / messages)

Rule slug: `no-magic-literals`.

Inline literals that carry meaning — route templates, status / type discriminators, user-facing or log messages, configuration keys, header / claim names, magic numbers — are forbidden in executable code. They must be **named**: a **constant class** for free-form strings or numbers, and **preferably an `enum`** for any closed set of values.

## What it looks like

```csharp
// Banned — inline route template
var response = await Http.PostAsJsonAsync($"api/apis/{ApimApiId}/identities", payload);

// Banned — string-typed status compared against string literals (a closed set → must be an enum)
_wizardScopes = _scopes
    .Where(s => s.Status is "Registered" or "Custom")
    .Select(s => new WizardScopeModel { ScopeName = s.ScopeName, SelectedType = s.ScopeType })
    .ToList();

// Banned — inline user-facing message (here also non-English and leaking ex.Message)
catch (Exception ex)
{
    _wizardError = $"Error creating identity: {ex.Message}";
    _wizardStep  = 1;
}

// Banned — magic number with no named intent
if (attempt > 3) Abort();
```

## Why it's banned

1. **A literal repeated across files drifts.** The same route / status / key typed by hand in five places becomes five subtly different strings after the first refactor; the compiler cannot catch the divergence.
2. **Closed sets belong in the type system.** A status that can only be `Registered` / `Custom` / … is an `enum`; comparing `string` literals discards exhaustiveness checking, allocates, and is case-fragile.
3. **Messages and routes are contracts.** Centralising them (one `ApiRoutes`, one `ErrorMessages`) makes them greppable, reviewable, and translatable — the precondition for the user-facing-message and i18n rules.
4. **Magic numbers hide intent.** `if (attempt > 3)` says nothing; `if (attempt > RetryPolicy.MaxAttempts)` does.

## What to do instead

```csharp
// Routes — a constant class; compose with string.Format over the named template
public static class ApiRoutes
{
    public const string Identities = "api/apis/{0}/identities";
}

var response = await Http.PostAsJsonAsync(string.Format(ApiRoutes.Identities, apimApiId), payload);

// Closed set — an enum, not strings
public enum ScopeStatus { Registered, Custom /* … */ }

_wizardScopes = _scopes
    .Where(s => s.Status is ScopeStatus.Registered or ScopeStatus.Custom)
    .Select(s => new WizardScopeModel { ScopeName = s.ScopeName, SelectedType = s.ScopeType })
    .ToList();

// Message — a named, generic, English constant (see exceptions-logged-not-leaked.md)
public static class ErrorMessages
{
    public const string CreateIdentity = "The identity could not be created. Please try again.";
}

catch (Exception ex)
{
    LogCreateIdentityFailed(logger, apimApiId, ex);   // full detail to ILogger
    _wizardError = ErrorMessages.CreateIdentity;       // generic, named, English
    _wizardStep  = 1;
}
```

What stays allowed (not "magic"):

- Identity / neutral literals with no domain meaning: `0`, `1`, `-1`, `""`, `string.Empty`, `true` / `false`.
- The single **declaration site** of a constant or enum member — that site *is* the name.
- Format / template strings that live **inside** the constant class itself.
- Attribute arguments the framework requires as compile-time constants where no constant indirection is possible (rare — confine to the attribute, do not repeat the value elsewhere).

Naming homes:

- `ApiRoutes` / `*Routes` — route and endpoint templates (also lowercase + English; see [no-uppercase-routes.md](no-uppercase-routes.md), [../csharp-style/english-only.md](../csharp-style/english-only.md)).
- `ErrorMessages` / `*Messages` — user-facing and log message text.
- `enum` — any closed set of discriminators / states / kinds.
- `*Constants` / `*Keys` — configuration keys, header names, claim types, cache keys.

## Enforcement

- **Clean-as-you-touch:** inside any file you edit, replace the literal with a constant or enum reference; create the `ApiRoutes` / `ErrorMessages` / `enum` home if it does not exist. See [../build-quality/clean-as-you-touch.md](../build-quality/clean-as-you-touch.md).
- **Reviewer (`dotnet-reviewer`):** flag each meaningful inline literal. Severity `major` when the literal is duplicated or is a route / message / closed-set discriminator; `minor` when it is a single isolated occurrence with low drift risk. A closed set compared as strings is `major` — the fix is an `enum`, not a string constant.
- **Names are English.** A constant or enum that merely renames a Spanish literal still violates [../csharp-style/english-only.md](../csharp-style/english-only.md).

## See also

- [exceptions-logged-not-leaked.md](exceptions-logged-not-leaked.md) — `ErrorMessages` is the home for the generic user message that replaces a leaked `ex.Message`.
- [../csharp-style/english-only.md](../csharp-style/english-only.md) — enum members, constant names, and route segments are English-only.
- [no-uppercase-routes.md](no-uppercase-routes.md) — route templates are lowercase (and belong in `ApiRoutes`).
- [../build-quality/clean-as-you-touch.md](../build-quality/clean-as-you-touch.md) — scope-bounded eradication policy.
