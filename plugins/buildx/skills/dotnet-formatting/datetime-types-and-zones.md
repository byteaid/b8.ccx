# Date/Time Types, `TimeProvider`, and `TimeZoneInfo`

## Date/time types — choosing & `TimeProvider`

| Type | Stores | Use for |
|---|---|---|
| `DateTime` | date+time + `Kind` (Local/Utc/Unspecified) | Legacy code, abstract or UTC-only data, math where DST is irrelevant. |
| `DateTimeOffset` | date+time + offset from UTC | **Default** for unambiguous points in time. |
| `DateOnly` (.NET 6+) | date | Birth dates, holidays, business dates, SQL `date`. |
| `TimeOnly` (.NET 6+) | time of day, 00:00:00.0000000–23:59:59.9999999 | Daily schedules, opening hours. |
| `TimeSpan` | duration (signed, ±10675199 days) | Elapsed time, intervals. **Not** a time of day. |
| `TimeZoneInfo` | a time zone | Conversions, DST queries. |
| `TimeProvider` (.NET 8+) | abstract clock | Inject for testability; replaces `DateTime.UtcNow` / `Task.Delay`. |

## `TimeProvider`

```csharp
public abstract class TimeProvider
{
    public static TimeProvider System { get; }
    public virtual DateTimeOffset GetUtcNow();
    public DateTimeOffset GetLocalNow();
    public virtual TimeZoneInfo LocalTimeZone { get; }
    public virtual long GetTimestamp();
    public TimeSpan GetElapsedTime(long startingTimestamp);
    public virtual ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period);
}

public class Service(TimeProvider clock)
{
    public bool IsStale(DateTimeOffset created)
        => clock.GetUtcNow() - created > TimeSpan.FromHours(1);
}

public sealed class FakeClock(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan d) => _now = _now.Add(d);
}
```

`Task.Delay(TimeSpan, TimeProvider)` and `CancellationTokenSource(TimeSpan, TimeProvider)` accept it directly.

## `DateTime.Kind` rules

| Kind | `K` output | Conversions assume |
|---|---|---|
| `Utc` | `Z` | already UTC |
| `Local` | system offset (e.g. `-07:00`) | local zone |
| `Unspecified` | empty | local (legacy default) |

Use `DateTime.SpecifyKind(d, DateTimeKind.Utc)` to label without conversion.

## Time zones (`TimeZoneInfo`)

```csharp
TimeZoneInfo.Utc;
TimeZoneInfo.Local;
TimeZoneInfo.GetSystemTimeZones();
TimeZoneInfo.FindSystemTimeZoneById(string id);                     // throws if missing
TimeZoneInfo.TryFindSystemTimeZoneById(string id, out var tz);      // .NET 6+

DateTime tz.ConvertTimeToUtc(DateTime);
DateTime tz.ConvertTimeFromUtc(DateTime, TimeZoneInfo destination);
DateTime tz.ConvertTime(DateTime, TimeZoneInfo source, TimeZoneInfo dest);
DateTimeOffset tz.ConvertTime(DateTimeOffset, TimeZoneInfo dest);

bool tz.IsDaylightSavingTime(DateTime);
TimeSpan tz.GetUtcOffset(DateTime);
bool tz.IsInvalidTime(DateTime);    // spring-forward gap
bool tz.IsAmbiguousTime(DateTime);  // fall-back overlap
```

### IANA vs Windows TZ IDs

- **Windows IDs** (e.g. `"Pacific Standard Time"`) — historic Windows format.
- **IANA IDs** (e.g. `"America/Los_Angeles"`) — standard everywhere else and on the Unicode CLDR.
- .NET 6+: `FindSystemTimeZoneById` accepts **either** form on **any OS**.
- Helpers: `tz.HasIanaId`; `TimeZoneInfo.TryConvertIanaIdToWindowsId(...)`; `TryConvertWindowsIdToIanaId(windowsId, [region], out ianaId)`.

```csharp
if (TimeZoneInfo.TryFindSystemTimeZoneById("America/Los_Angeles", out var la)) { /* … */ }
var madrid = TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");
var local  = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, madrid);
```

DST hazards:

- `IsInvalidTime`: a `DateTime` in the spring-forward gap. Converting throws `ArgumentException`.
- `IsAmbiguousTime`: a `DateTime` in the fall-back overlap. `ConvertTime` picks the standard (post-fall-back) interpretation.
- For unambiguous storage: persist `DateTimeOffset` in UTC + IANA TZ ID alongside if you also need the original location.

## Cheat sheet

- **Do** persist timestamps as `DateTimeOffset` in UTC + IANA TZ id.
- **Do** use `CultureInfo.InvariantCulture` for any I/O.
- **Do** prefer `O`/`o` for round-trip and `R`/`r` for HTTP/RFC1123.
- **Do** prefer source-generated regex over `RegexOptions.Compiled`.
- **Do** pass a `MatchTimeout` on every regex that touches user input.
- **Do** use `Try…` variants and `Span` / `Utf8Span` overloads on hot paths.
- **Do** inject `TimeProvider` instead of calling `DateTime.UtcNow`.
- **Don't** use `DateTime.Parse` on machine-generated wire formats — use `ParseExact` with `InvariantCulture`.
- **Don't** mix `DateTimeKind.Unspecified` with TZ conversions — set `Kind` explicitly.
- **Don't** rely on `R` to convert to UTC — it doesn't.
- **Don't** rely on `Double.ToString("R")` for round-tripping; use `G17`.
- **Don't** treat `TimeSpan` as a time-of-day; use `TimeOnly`.
- **Don't** assume Windows TZ IDs work everywhere; stored data should be **IANA**.
