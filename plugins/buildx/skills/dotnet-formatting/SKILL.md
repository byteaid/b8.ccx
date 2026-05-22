---
name: dotnet-formatting
description: Formatting, parsing, regex, date/time, and culture reference for .NET 10 / C# 14. Covers standard + custom numeric and date/time format strings, composite formatting, string interpolation internals (`DefaultInterpolatedStringHandler`), `IFormattable`/`ISpanFormattable`/`IUtf8SpanFormattable`/`ICustomFormatter`, parsing (`TryParse`/`ParseExact`/`IParsable<T>`, `NumberStyles`, `DateTimeStyles`), `CultureInfo`/`NumberFormatInfo`/`DateTimeFormatInfo`, regex (engine, `RegexOptions`, source-generated `[GeneratedRegex]`, catastrophic backtracking + `MatchTimeout`), date/time type selection (`DateTime` vs `DateTimeOffset` vs `DateOnly`/`TimeOnly`/`TimeSpan`), `TimeProvider` for testability, and `TimeZoneInfo` (IANA ↔ Windows IDs, DST).
when_to_use: |
  - Trigger keywords: ToString, format string, TryParse, ParseExact, NumberStyles, DateTimeStyles, ISpanFormattable, IUtf8SpanFormattable, IParsable, CultureInfo, InvariantCulture, DefaultInterpolatedStringHandler, FormattableString.Invariant, Regex, GeneratedRegex, NonBacktracking, MatchTimeout, DateTimeOffset, DateOnly, TimeOnly, TimeProvider, TimeZoneInfo, IANA.
  - Task shapes: format a number/date for a wire protocol; round-trip a `DateTimeOffset`; pick a culture-invariant format; parse user input safely; replace `RegexOptions.Compiled` with `[GeneratedRegex]`; cap regex with a timeout; fix catastrophic backtracking; inject a fake clock with `TimeProvider`; convert IANA ↔ Windows TZ IDs; implement `ISpanFormattable` on a value type.
allowed-tools: Bash, Edit, Glob, Grep, Monitor, NotebookEdit, PowerShell, Read, Write
user-invocable: false
paths: ["**/*.cs"]
---

# .NET Formatting, Parsing & Regex — Reference

Reference for converting between strings and CLR values on .NET 10. JSON belongs to `dotnet-serialization`; this file owns numeric/date/regex/culture.

## Mental model

```
input ─┬─ Parse / TryParse / ParseExact            ─► CLR value
       └─ IParsable<T>.Parse / TryParse (.NET 7+)

CLR value
   ├─ ToString()                          (no format string, current culture)
   ├─ ToString(format, IFormatProvider?)  (IFormattable)
   ├─ TryFormat(Span<char>, ...)          (ISpanFormattable, .NET 6+)
   ├─ TryFormat(Span<byte>, ...)          (IUtf8SpanFormattable, .NET 8+)
   └─ string.Format / interpolation / StringBuilder.AppendFormat
                                          (composite formatting)
```

- A *standard* format string = single alphabetic char (`C`, `D`, `N`, …) optionally followed by a precision integer.
- Any format string with **>1 alphabetic char** is parsed as a *custom* format string.
- `null` provider = `CultureInfo.CurrentCulture`. For deterministic output use `CultureInfo.InvariantCulture`.
- Precision specifier max: **999,999,999** (.NET 7+).
- Floating-point midpoint rounding: `MidpointRounding.ToEven` on .NET Core 2.1+.
- Cross-platform globalization is unified via ICU on .NET 5+.

## Non-negotiable rules

1. **`InvariantCulture` for any I/O** — logs, files, JSON, RFC-formatted protocols. Anything machine-readable.
2. **`ParseExact` for machine-generated wire formats.** `DateTime.Parse` accepts only what the culture knows about.
3. **Always pass a `MatchTimeout`** on regex that touches user input. Default is `Regex.InfiniteMatchTimeout`.
4. **Source-generated regex (`[GeneratedRegex]`) over `RegexOptions.Compiled`.** AOT-friendly, debuggable, trimmable; the generator ignores `Compiled`.
5. **`DateTimeOffset` is the default** for unambiguous points in time. Use `DateOnly` / `TimeOnly` for calendar / clock-only domains; `TimeSpan` is a duration, not a time of day.
6. **Inject `TimeProvider`** rather than calling `DateTime.UtcNow` / `Stopwatch` directly; tests need to replace the clock.
7. **Persist time zones as IANA IDs.** .NET 6+ accepts both IANA and Windows IDs everywhere, but storage stays IANA.
8. **`R`/`r` does NOT convert to UTC** — it just stamps `GMT`. Convert to UTC first, or use `O` on a `DateTimeOffset`.

## Sub-index

| Topic | File | Load when |
|---|---|---|
| Standard + custom numeric format strings; numeric parsing (`NumberStyles`, `IParsable<T>`) | [numeric-format-and-parsing.md](numeric-format-and-parsing.md) | Picking a numeric format spec; parsing user/machine numbers; sections; binary/hex specifiers. |
| Standard + custom date/time format strings; `DateTimeStyles` parsing | [datetime-format-and-parsing.md](datetime-format-and-parsing.md) | Round-tripping `DateTime`/`DateTimeOffset`; parsing wire-format dates with `ParseExact`. |
| Composite formatting + interpolation internals; `IFormattable`/`ISpanFormattable`/`IUtf8SpanFormattable`/`ICustomFormatter`; culture; trimming/padding/splitting | [composite-and-formattable.md](composite-and-formattable.md) | Implementing custom format on a value type; building app-wide invariant defaults; allocation-free formatting. |
| `Regex` API, `RegexOptions`, syntax cheat sheet, `[GeneratedRegex]`, catastrophic-backtracking & timeout | [regex.md](regex.md) | Writing or auditing regex; replacing `Compiled` with source-generation; non-backtracking engine. |
| `DateTime` vs `DateTimeOffset` vs `DateOnly`/`TimeOnly`/`TimeSpan`; `TimeProvider`; `TimeZoneInfo` (IANA ↔ Windows, DST) | [datetime-types-and-zones.md](datetime-types-and-zones.md) | Choosing a date/time type; injecting a clock; converting between time zones; DST hazards. |

## Cross-references

- Public docs (Standard numeric format): https://learn.microsoft.com/en-us/dotnet/standard/base-types/standard-numeric-format-strings
- Public docs (Custom numeric format): https://learn.microsoft.com/en-us/dotnet/standard/base-types/custom-numeric-format-strings
- Public docs (Standard date/time format): https://learn.microsoft.com/en-us/dotnet/standard/base-types/standard-date-and-time-format-strings
- Public docs (Custom date/time format): https://learn.microsoft.com/en-us/dotnet/standard/base-types/custom-date-and-time-format-strings
- Public docs (Composite formatting): https://learn.microsoft.com/en-us/dotnet/standard/base-types/composite-formatting
- Public docs (Parsing numeric): https://learn.microsoft.com/en-us/dotnet/standard/base-types/parsing-numeric
- Public docs (Parsing date/time): https://learn.microsoft.com/en-us/dotnet/standard/base-types/parsing-datetime
- Public docs (Trimming): https://learn.microsoft.com/en-us/dotnet/standard/base-types/trimming
- Public docs (Regex overview): https://learn.microsoft.com/en-us/dotnet/standard/base-types/regular-expressions
- Public docs (Regex quick reference): https://learn.microsoft.com/en-us/dotnet/standard/base-types/regular-expression-language-quick-reference
- Public docs (Regex source generators): https://learn.microsoft.com/en-us/dotnet/standard/base-types/regular-expression-source-generators
- Public docs (Choosing date/time types): https://learn.microsoft.com/en-us/dotnet/standard/datetime/choosing-between-datetime
- Public docs (Time zones): https://learn.microsoft.com/en-us/dotnet/standard/datetime/converting-between-time-zones
- Related skill: `dotnet-serialization` — JSON/XML formatting for documents.
- Related skill: `dotnet-io` — text encoding for streams (`StreamReader`/`Writer`, BOM, `Encoding.UTF8`).
- Related skill: `dotnet-extensions` — `ILogger` message templates and structured logging.
- Related skill: `dotnet-networking` — HTTP date headers and content negotiation.
