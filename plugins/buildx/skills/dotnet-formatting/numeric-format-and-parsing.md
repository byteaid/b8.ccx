# Numeric Format Strings and Parsing

## Standard numeric format strings

| Spec | Name | Types | Precision | Default |
|---|---|---|---|---|
| `B`/`b` | Binary (.NET 8+) | integral | min digits, zero-padded | min required |
| `C`/`c` | Currency | all numeric | decimal digits | `NumberFormatInfo.CurrencyDecimalDigits` |
| `D`/`d` | Decimal | integral | min digits | min required |
| `E`/`e` | Exponential | all numeric | digits after decimal | 6 |
| `F`/`f` | Fixed-point | all numeric | decimal digits | `NumberDecimalDigits` |
| `G`/`g` | General (compact F or E) | all numeric | significant digits | Half=5, Single=7, Double=15, Decimal=29 |
| `N`/`n` | Number with group separators | all numeric | decimal digits | `NumberDecimalDigits` |
| `P`/`p` | Percent (×100) | all numeric | decimal digits | `PercentDecimalDigits` |
| `R`/`r` | Round-trip | `Single`, `Double`, `BigInteger` | ignored | — |
| `X`/`x` | Hex | integral | min digits | min required |

Notes: prefer `G17`/`G9` over `R` for `Double`/`Single` (R is reliable only for `BigInteger`). Infinity/NaN ignore the format string and emit `NumberFormatInfo` symbols.

```csharp
var inv = CultureInfo.InvariantCulture;
123.456m.ToString("C2", CultureInfo.GetCultureInfo("en-US")); // $123.46
123.456m.ToString("C2", CultureInfo.GetCultureInfo("fr-FR")); // 123,46 €
42.ToString("B");                       // 101010
255.ToString("b16");                    // 0000000011111111
(-1234).ToString("D6");                 // -001234
12345.6789.ToString("E", inv);          // 1.234568E+004
1234.567.ToString("N3", inv);           // 1,234.567
(-0.39678).ToString("P1", inv);         // -39.7 %
255.ToString("x4");                     // 00ff
```

## Custom numeric format strings

| Specifier | Meaning |
|---|---|
| `0` | Zero placeholder. Forces a digit; pads with `0`. |
| `#` | Digit placeholder. Forces a digit; emits nothing if absent. |
| `.` | Decimal point. |
| `,` | (a) Group separator between digit placeholders straddling integral digits; (b) Scaling when placed *immediately to the left of the decimal point* — divides by 1000 per comma. |
| `%` | Percent placeholder — multiplies by 100, inserts `PercentSymbol`. |
| `‰` (U+2030) | Per-mille — multiplies by 1000. |
| `E0`/`E+0`/`E-0` (`e0`/`e+0`/`e-0`) | Scientific notation. |
| `\` | Escape next char as literal. |
| `'…'` `"…"` | Literal string delimiter. |
| `;` | Section separator. 1/2/3 sections: all / non-neg+neg / pos+neg+zero. |

Section semantics (with section separator `;`): when negative is rendered through a section, the minus sign is **not** auto-inserted — encode it in the literal pattern.

```csharp
double v = 1234.5678; var inv = CultureInfo.InvariantCulture;
v.ToString("00000.00", inv);               // 01234.57
v.ToString("#.##", inv);                   // 1234.57
1234567890.ToString("(###) ###-####");     // (123) 456-7890
2147483647.ToString("##,#", inv);          // 2,147,483,647
2147483647.ToString("#,#,,", inv);         // 2,147   (scaled /1e6)
0.086.ToString("#0.##%", inv);             // 8.6%
86000.0.ToString("0.###E+000", inv);       // 8.6E+004

string fmt = "##;(##);**Zero**";
1234.0.ToString(fmt);   // 1234
(-1234.0).ToString(fmt);// (1234)
0.0.ToString(fmt);      // **Zero**
```

## Parsing numbers

```csharp
T  Parse(string s);
T  Parse(string s, NumberStyles style);
T  Parse(string s, IFormatProvider? p);
T  Parse(string s, NumberStyles style, IFormatProvider? p);
T  Parse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? p);  // .NET Core+
T  Parse(ReadOnlySpan<byte> utf8, NumberStyles style, IFormatProvider? p); // .NET 8+
bool TryParse(...) // matching overloads
```

Generic shape (.NET 7+, every numeric type implements):

```csharp
public interface IParsable<TSelf>            { static TSelf Parse(string s, IFormatProvider? p); static bool TryParse(string? s, IFormatProvider? p, out TSelf r); }
public interface ISpanParsable<TSelf>        : IParsable<TSelf> { static TSelf Parse(ReadOnlySpan<char> s, IFormatProvider? p); static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? p, out TSelf r); }
public interface IUtf8SpanParsable<TSelf>    /* .NET 8+ */ { static TSelf Parse(ReadOnlySpan<byte> utf8, IFormatProvider? p); static bool TryParse(ReadOnlySpan<byte> utf8, IFormatProvider? p, out TSelf r); }
```

### `NumberStyles`

| Flag | Effect |
|---|---|
| `None` | Digits only. |
| `AllowDecimalPoint` | Decimal separator + fractional digits. |
| `AllowExponent` | `e`/`E`. |
| `AllowLeading/TrailingWhite` | Whitespace at ends. |
| `AllowLeading/TrailingSign` | `+`/`-`. |
| `AllowParentheses` | `(123)` for negatives. |
| `AllowThousands` | Group separator. |
| `AllowCurrencySymbol` | `CurrencySymbol`. |
| `AllowHexSpecifier` | Integral only. 0-9, A-F, a-f. |
| `AllowBinarySpecifier` (.NET 8+) | Integral only. 0,1. |

Composite shortcuts: `Integer` (default for integer Parse) / `Number` / `Float` / `Currency` / `Any` / `HexNumber` / `BinaryNumber`.

```csharp
double.Parse("1,304.16", us);                                              // 1304.16
double.Parse("1 304,16", fr);                                              // 1304.16
int.Parse("1,304", NumberStyles.Integer | NumberStyles.AllowThousands);    // 1304
int.Parse("FF", NumberStyles.HexNumber);                                   // 255
int.Parse("1010", NumberStyles.AllowBinarySpecifier);                      // 10  (.NET 8+)
decimal.TryParse("(123.45)", NumberStyles.Number | NumberStyles.AllowParentheses, us, out var d); // -123.45

// BigInteger binary parsing — sign-bit semantics
BigInteger.Parse("11", NumberStyles.AllowBinarySpecifier);   // -1
BigInteger.Parse("011", NumberStyles.AllowBinarySpecifier);  //  3
```

Pitfalls: only ASCII digits 0-9 accepted (fullwidth/Arabic-Indic/Bangla throw `FormatException`); without a provider, current thread culture is used; for machine I/O always pass `CultureInfo.InvariantCulture`.
