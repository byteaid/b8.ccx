# Forbidden — Warning suppression

## What it looks like

```csharp
#pragma warning disable CS8600
string? maybeNull = SomeApi();
string definitelyNotNull = maybeNull;     // hides a real bug
#pragma warning restore CS8600

[SuppressMessage("Performance", "CA1848:Use LoggerMessage delegates")]
public void DoWork() => logger.LogInformation($"Working on {item}");

// .editorconfig
dotnet_diagnostic.CA1062.severity = none
```

```xml
<!-- Acme.Foo.WebAPI.csproj -->
<PropertyGroup>
  <NoWarn>CS8600;CS8602;CA1848</NoWarn>
</PropertyGroup>
```

## Why it's banned

1. **Warnings indicate real problems** — nullable reference flow errors, allocation hot paths, async/await bugs, deprecated API usage. Suppressing them does not fix them.
2. **Suppressions accumulate.** Today's `#pragma warning disable` is tomorrow's "we always had it". The compiler stops talking, the bugs stay.
3. **The team's gate is `dotnet build -warnaserror` with zero warnings.** Hiding warnings to slip past the gate defeats the gate.
4. **Cascading failures.** When a warning suppression hides a real bug, the failure surfaces in production with a runtime exception that has no analyzer trail.

## What to do instead

Fix the root cause. Examples:

| Warning | Real fix |
|---|---|
| `CS8600` "converting null literal to non-nullable" | Make the assignment safe (`maybeNull ?? throw`, `is null` check), or change the target to nullable. |
| `CS8602` "dereference of possibly null" | Null-check or use `?.` / `??`. |
| `CA1848` "use LoggerMessage delegates" | Convert the `logger.LogXxx($"...")` to a `[LoggerMessage]` partial method. See [../source-generators/loggermessage.md](../source-generators/loggermessage.md). |
| `CA1062` "validate arguments of public methods" | Add an `ArgumentNullException.ThrowIfNull(...)` call. |
| `CS0618` "obsolete API" | Migrate to the replacement API — never silence the warning. |

If a warning is genuinely unfixable in your task scope (cascading refactor), STOP and report — do not suppress to keep moving.

## Enforcement

- **On sight, inside a file you're editing:** remove the suppression and fix the root cause. See [../build-quality/clean-as-you-touch.md](../build-quality/clean-as-you-touch.md).
- **On review:** any new `#pragma warning disable`, `[SuppressMessage]`, or `<NoWarn>` is a blocking finding.
- **Build gate:** every project sets `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`. See [../build-quality/zero-warnings-rule.md](../build-quality/zero-warnings-rule.md).

## Exceptions

Vanishingly few. If a third-party analyzer emits a false positive that has been verified upstream as a bug, document the suppression with a one-line comment citing the issue URL and bound it to the smallest possible scope. This is the only legitimate case.
