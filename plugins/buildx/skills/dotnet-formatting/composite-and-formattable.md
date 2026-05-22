# Composite Formatting, Interpolation, IFormattable, Culture, Trimming

## Composite formatting

Item syntax: `{index[,alignment][:formatString]}`

- `index` ≥ 0; outside-of-bounds → `FormatException`.
- `alignment` is signed; positive = right-align, negative = left-align.
- Brace literals: `{{` and `}}`.

Surface APIs: `string.Format`, `StringBuilder.AppendFormat`, `Console.Write/WriteLine`, `Debug.WriteLine`, `Trace.*`, C# interpolated strings `$"..."` (compiled into `DefaultInterpolatedStringHandler`), `FormattableString.Invariant($"...")`.

```csharp
string.Format("{0,-20} {1,5:N1}", "Adam", 40m);              // "Adam                  40.0"
string.Format("0x{0:X} {0:E} {0:N}", long.MaxValue);
string.Format(CultureInfo.InvariantCulture, "{0:C2}", 100m); // ¤100.00 (invariant currency)
string inv = FormattableString.Invariant($"{DateTime.UtcNow:O}");
```

### Per-item processing order

1. `null` value → `""`.
2. If a non-null `IFormatProvider` provides an `ICustomFormatter`, call `Format(formatString, value, provider)`. Non-null result wins.
3. Else if value implements `IFormattable`, call `value.ToString(formatString, provider)`.
4. Else call `value.ToString()`.
5. Apply alignment padding.

## C# string interpolation internals

`$"{name,-10}: {price:C2}"` compiles to `DefaultInterpolatedStringHandler` (.NET 6+):

```csharp
var h = new DefaultInterpolatedStringHandler(literalLen, formattedCount);
h.AppendLiteral("...");
h.AppendFormatted(name, alignment: -10);
h.AppendFormatted(price, format: "C2");
string s = h.ToStringAndClear();
```

### Custom interpolated-string handlers

Mark a `ref struct` with `[InterpolatedStringHandler]` and a constructor taking `(int literalLen, int formattedCount, ..., out bool enabled)`. Useful for skipping formatting work entirely when a log level is filtered out (zero allocations).

```csharp
[InterpolatedStringHandler]
public ref struct LogHandler
{
    StringBuilder? _sb;
    public LogHandler(int literalLen, int formattedCount, LogLevel min, LogLevel current, out bool enabled)
    {
        enabled = current >= min;
        _sb = enabled ? new StringBuilder(literalLen) : null;
    }
    public void AppendLiteral(string s) => _sb?.Append(s);
    public void AppendFormatted<T>(T value) => _sb?.Append(value);
    public void AppendFormatted<T>(T value, string? format) where T : IFormattable
        => _sb?.Append(value?.ToString(format, null));
    public override string ToString() => _sb?.ToString() ?? "";
}

public static void Log(LogLevel level,
    [InterpolatedStringHandlerArgument("level")] ref LogHandler msg) { /* ... */ }
```

## `IFormattable` / `ISpanFormattable` / `IUtf8SpanFormattable`

```csharp
public interface IFormattable
{
    string ToString(string? format, IFormatProvider? formatProvider);
}

public interface ISpanFormattable : IFormattable
{
    bool TryFormat(Span<char> destination, out int charsWritten,
                   ReadOnlySpan<char> format, IFormatProvider? provider);
}

public interface IUtf8SpanFormattable
{
    bool TryFormat(Span<byte> utf8Destination, out int bytesWritten,
                   ReadOnlySpan<char> format, IFormatProvider? provider);
}
```

Implemented by every primitive numeric type, `Char`, `DateOnly`, `DateTime`, `DateTimeOffset`, `Decimal`, `Double`, `Guid`, `Half`, `Int128`/`UInt128`, `IPAddress`, `IPEndPoint`, `IPNetwork`, `BigInteger`, `Complex`, `NFloat`, `Rune`, `TimeOnly`, `TimeSpan`, `Version`, plus the generic numeric interfaces (`INumberBase<TSelf>`, `IBinaryInteger<TSelf>`, …).

```csharp
public readonly struct Money : IFormattable, ISpanFormattable, IUtf8SpanFormattable
{
    public decimal Amount { get; init; }
    public string Currency { get; init; }

    public override string ToString() => ToString(null, CultureInfo.CurrentCulture);

    public string ToString(string? format, IFormatProvider? provider)
        => string.Create(provider, $"{Amount.ToString(format ?? "F2", provider)} {Currency}");

    public bool TryFormat(Span<char> destination, out int charsWritten,
                          ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        if (!Amount.TryFormat(destination, out int n, format.IsEmpty ? "F2" : format, provider))
        { charsWritten = 0; return false; }
        if (destination.Length < n + 1 + Currency.Length) { charsWritten = 0; return false; }
        destination[n] = ' ';
        Currency.AsSpan().CopyTo(destination[(n + 1)..]);
        charsWritten = n + 1 + Currency.Length;
        return true;
    }

    public bool TryFormat(Span<byte> utf8, out int bytesWritten,
                          ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        if (!Amount.TryFormat(utf8, out int n, format.IsEmpty ? "F2" : format, provider))
        { bytesWritten = 0; return false; }
        int needed = n + 1 + System.Text.Encoding.UTF8.GetByteCount(Currency);
        if (utf8.Length < needed) { bytesWritten = 0; return false; }
        utf8[n] = (byte)' ';
        System.Text.Encoding.UTF8.GetBytes(Currency, utf8[(n + 1)..]);
        bytesWritten = needed;
        return true;
    }
}
```

### `ICustomFormatter` + `IFormatProvider`

```csharp
public class HexEverythingFormatter : IFormatProvider, ICustomFormatter
{
    public object? GetFormat(Type? formatType)
        => formatType == typeof(ICustomFormatter) ? this : null;

    public string Format(string? format, object? arg, IFormatProvider? provider)
    {
        if (arg is IFormattable && format == "HEX")
            return Convert.ToString(Convert.ToInt64(arg), 16).ToUpperInvariant();
        return arg?.ToString() ?? "";
    }
}
```

## Culture / NumberFormatInfo / DateTimeFormatInfo

| Type | Purpose |
|---|---|
| `CultureInfo.InvariantCulture` | Locale-neutral. Use for I/O. |
| `CultureInfo.CurrentCulture` | Per-thread; affects formatting/parsing when no provider passed. |
| `CultureInfo.CurrentUICulture` | Per-thread; affects resource lookup, *not* formatting. |
| `CultureInfo.GetCultureInfo("xx-YY")` | Cached, read-only. Prefer over `new CultureInfo(...)`. |
| `NumberFormatInfo` | Numeric format settings. |
| `DateTimeFormatInfo` | Date/time format settings. |

App-wide invariant default for service-style apps:

```csharp
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
```

## Trimming, padding, removing

| Method | Notes |
|---|---|
| `Trim()` / `Trim(char)` / `Trim(params char[])` / `Trim(ReadOnlySpan<char>)` | Whitespace or specified chars. Span overload allocation-free. |
| `TrimStart(...)` / `TrimEnd(...)` | Same shapes. |
| `Remove(int start[, int count])` | Cut. |
| `Replace(string old, string? new[, StringComparison])` | Replace all. `null` removes. |
| `PadLeft/PadRight(int totalWidth, char paddingChar = ' ')` | Pad. |
| `Substring(int start[, int length])` | Slice. Prefer `AsSpan()` slicing on hot paths. |
| `Split(char\|string\|char[]\|string[], StringSplitOptions)` | `RemoveEmptyEntries`, `TrimEntries` (.NET 5+). |
| `string.Concat`, `string.Join` | Joining. |
| `string.IsNullOrEmpty/IsNullOrWhiteSpace` | Predicates. |

```csharp
"  Hello   ".Trim();                     // "Hello"
"* Title *".Trim([' ', '*']);            // "Title"
"a,,b,,c".Split(",,", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
ReadOnlySpan<char> span = "  42  ".AsSpan().Trim();
int n = int.Parse(span, CultureInfo.InvariantCulture); // no string allocation
```
