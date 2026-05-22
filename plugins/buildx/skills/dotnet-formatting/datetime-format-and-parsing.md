# Date / Time Format Strings and Parsing

## Standard date & time format strings

| Spec | Pattern | Example (en-US, 2009-06-15T13:45:30) |
|---|---|---|
| `d` | `ShortDatePattern` | `6/15/2009` |
| `D` | `LongDatePattern` | `Monday, June 15, 2009` |
| `f`/`F` | LongDate + Short/LongTime | `Monday, June 15, 2009 1:45[:30] PM` |
| `g`/`G` | ShortDate + Short/LongTime | `6/15/2009 1:45[:30] PM` |
| `M`/`m` | `MonthDayPattern` | `June 15` |
| `O`/`o` | `yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'fffffffK` | `2009-06-15T13:45:30.0000000-07:00` |
| `R`/`r` | `RFC1123Pattern` `ddd, dd MMM yyyy HH':'mm':'ss 'GMT'` | `Mon, 15 Jun 2009 13:45:30 GMT` |
| `s` | `SortableDateTimePattern` `yyyy'-'MM'-'dd'T'HH':'mm':'ss` | `2009-06-15T13:45:30` |
| `t`/`T` | Short/Long time | `1:45[:30] PM` |
| `u` | `yyyy'-'MM'-'dd HH':'mm':'ss'Z'` | `2009-06-15 13:45:30Z` |
| `U` | full universal (`FullDateTimePattern`) | `Monday, June 15, 2009 8:45:30 PM` |
| `Y`/`y` | `YearMonthPattern` | `June 2009` |

Use `O`/`R`/`s`/`u` for serialization (culture-invariant). `R` does **not** convert to UTC; convert first or use `O` on a `DateTimeOffset`. `U` converts to UTC then formats long. `O` round-trips `DateTimeKind` via `K` (`Z`, `±HH:mm`, or empty for `Unspecified`).

`DateOnly` accepts: `d`, `D`, `M`/`m`, `O`/`o`, `R`/`r`, `Y`/`y`. `TimeOnly` accepts: `t`, `T`, `O`/`o`, `R`/`r`.

## Custom date & time format strings

A *single* custom char must be prefixed with `%` to disambiguate from a standard specifier (e.g. `dt.ToString("%h")`).

| Spec | Meaning | Example |
|---|---|---|
| `d` / `dd` / `ddd` / `dddd` | Day of month (no/leading 0) / abbrev / full name | `1` / `01` / `Mon` / `Monday` |
| `f`–`fffffff` | Tenths…ten-millionths of second, always emitted | `f`→`6`, `fff`→`617` |
| `F`–`FFFFFFF` | Same, but trailing zeros suppressed | |
| `g` / `gg` | Era | `A.D.` |
| `h` / `hh` | 12-hour (no/leading 0) | `1` / `01` |
| `H` / `HH` | 24-hour (no/leading 0) | `13` / `13` |
| `K` | `Kind` ↔ `Z` / `±HH:mm` / empty (Unspecified) | `-07:00` |
| `m` / `mm` | Minute | `9` / `09` |
| `M` / `MM` / `MMM` / `MMMM` | Month | `6` / `06` / `Jun` / `June` |
| `s` / `ss` | Second | `9` / `09` |
| `t` / `tt` | First char / full AM/PM | `P` / `PM` |
| `y` … `yyyyy` | Year width | `1` / `01` / `2009` |
| `z` / `zz` / `zzz` | UTC offset (no LZ / LZ / `±HH:mm`) | `-7` / `-07` / `-07:00` |
| `:` / `/` | Time / date separator | (per culture) |
| `"…"` `'…'` | Literal | |
| `%c` | Use `c` as custom specifier | `%h` |
| `\c` | Escape `c` as literal | `\h` |

Reserved chars (always specifiers): `d f F g h H K m M s t y z % : / " ' \`. Anything else is literal.

Two-digit-year (`yy`) parsing uses `Calendar.TwoDigitYearMax` (default 2029); clone the calendar to override.

## Parsing dates & times

```csharp
DateTime          DateTime.Parse(string, IFormatProvider? = null, DateTimeStyles = None);
DateTime          DateTime.ParseExact(string, string format, IFormatProvider?, DateTimeStyles = None);
DateTime          DateTime.ParseExact(string, string[] formats, IFormatProvider?, DateTimeStyles);
bool              DateTime.TryParse(...);       // 4 overloads
bool              DateTime.TryParseExact(...);  // 2 overloads
// DateTimeOffset / DateOnly / TimeOnly: same shape, plus span/utf8 (.NET 7+/8+).
```

### `DateTimeStyles`

| Value | Effect |
|---|---|
| `None` | Default. |
| `AllowLeading/TrailingWhite` / `AllowInnerWhite` / `AllowWhiteSpaces` | Whitespace tolerance. |
| `NoCurrentDateDefault` | Missing year/month/day default to `1` instead of *today*. |
| `AdjustToUniversal` | After parsing, convert result to UTC. |
| `AssumeLocal` | If no offset present, assume local TZ. |
| `AssumeUniversal` | If no offset present, assume UTC. |
| `RoundtripKind` | Preserve `DateTimeKind` (use with `O`/`o`). |

Default behavior: missing time → midnight; missing date → today; missing year → current year; missing day → 1; ambiguous date (`02/03/04`) → resolved using the format provider's `ShortDatePattern` order; offset present → `Kind = Local`, value adjusted to local TZ unless `AdjustToUniversal`/`RoundtripKind`; no offset → `Kind = Unspecified`.

```csharp
DateTime.ParseExact("2025-01-15T14:30:00", "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);

string[] fmts = { "MM/dd/yyyy", "M/d/yyyy", "dd/MM/yyyy" };
DateOnly.ParseExact("1/15/2025", fmts, CultureInfo.InvariantCulture, DateTimeStyles.None);

// Round-trip
var s   = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture);
var dto = DateTimeOffset.ParseExact(s, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

// Force UTC
var utc = DateTime.Parse("2025-01-15T10:00:00", CultureInfo.InvariantCulture,
                         DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
```
