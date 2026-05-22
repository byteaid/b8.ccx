# Regular Expressions

Namespace `System.Text.RegularExpressions`. Engine is backtracking by default; alternative non-backtracking engine via `RegexOptions.NonBacktracking` (.NET 7+).

## Core API

```csharp
public sealed class Regex
{
    public Regex(string pattern, RegexOptions options = None, TimeSpan matchTimeout = default);

    public bool IsMatch(string input, int startat = 0);
    public bool IsMatch(ReadOnlySpan<char> input);
    public Match Match(string input, int startat = 0);
    public MatchCollection Matches(string input, int startat = 0);
    public string Replace(string input, string replacement, int count = -1, int startat = 0);
    public string Replace(string input, MatchEvaluator evaluator, ...);
    public string[] Split(string input, int count = -1, int startat = 0);
    public ValueMatchEnumerator EnumerateMatches(ReadOnlySpan<char> input);   // .NET 7+
    public ValueSplitEnumerator EnumerateSplits(ReadOnlySpan<char> input);    // .NET 9+

    // static (cached, up to ~15 patterns)
    public static bool   IsMatch(string input, string pattern, RegexOptions, TimeSpan);
    public static Match  Match (string input, string pattern, RegexOptions, TimeSpan);
    public static string Replace(string input, string pattern, string replacement, RegexOptions, TimeSpan);
}
```

`Match`: `Success`, `Index`, `Length`, `Value`, `ValueSpan` (.NET 7+), `Groups[name|index]`, `Captures`, `NextMatch()`. Named groups: `m.Groups["name"]`. `Group.Captures` lists every capture per quantifier iteration.

## `RegexOptions`

| Flag | Inline | Notes |
|---|---|---|
| `IgnoreCase` | `i` | Culture-aware unless `CultureInvariant`. |
| `Multiline` | `m` | `^`/`$` match line boundaries. |
| `ExplicitCapture` | `n` | Numbered groups become non-capturing. |
| `Compiled` | | Reflection.Emit IL. **Source generators preferred**; the generator ignores it. |
| `Singleline` | `s` | `.` matches `\n`. |
| `IgnorePatternWhitespace` | `x` | Ignore unescaped whitespace; allow `#` comments. |
| `RightToLeft` | | Match right-to-left. |
| `ECMAScript` | | ECMAScript subset. |
| `CultureInvariant` | | Invariant culture for case folding. |
| `NonBacktracking` (.NET 7+) | | Linear-time engine. **No** lookarounds, **no** backreferences, **no** atomic/balancing groups, **no** `RightToLeft`. Cannot combine with `Compiled` (silently ignored). |

## Syntax cheat sheet

Character escapes: `\a \t \r \n \v \f \e`; `\nnn` octal; `\xnn` hex; `\unnnn` Unicode; `\cX` control char.

Character classes: `[abc]` / `[^abc]`; `[a-z]`; `.` any-except-LF; `\w \W`; `\s \S`; `\d \D`; `\p{name}` `\P{name}` Unicode category/block.

Anchors: `^ $`; `\A` `\Z` `\z`; `\G` end of previous match; `\b` `\B` word boundary.

Grouping: `(expr)` capturing; `(?:expr)` non-capturing; `(?<name>expr)` / `(?'name'expr)` named; `(?<n1-n2>expr)` balancing; `(?>expr)` atomic; `(?=expr)` `(?!expr)` lookahead; `(?<=expr)` `(?<!expr)` lookbehind; `(?imnsx-imnsx:expr)` inline options.

Backreferences: `\1`…`\9` numbered; `\k<name>` named.

Alternation: `a|b`; `(?(expr)yes|no)` conditional on assertion; `(?(name)yes|no)` conditional on group match.

Quantifiers: `*` `+` `?` `{n}` `{n,}` `{n,m}` (greedy); add `?` for lazy.

Substitutions in `Replace`: `$1`…`$n`, `${name}`, `$$`, `$&` whole match, `` $` `` before, `$'` after, `$+` last group, `$_` entire input.

## Source-generated regex (`[GeneratedRegex]`)

Preferred over `Compiled`. AOT-friendly, debuggable, trimmable.

```csharp
public partial class Validators
{
    [GeneratedRegex(@"^\d{3}-\d{2}-\d{4}$", RegexOptions.CultureInvariant)]
    private static partial Regex SsnRegex();

    [GeneratedRegex(@"\b[\w\.-]+@[\w\.-]+\.\w+\b", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex EmailRegex { get; }   // .NET 9+ partial property

    public static bool IsSsn(string s) => SsnRegex().IsMatch(s);
}
```

Constraints: method/property must be `partial`, return `Regex`, take no arguments. Pattern + options + culture must be compile-time constants. If the pattern uses an unsupported construct (`IgnoreCase` backreferences, `NonBacktracking`), the generator falls back to caching a normal `Regex`.

## Best practices & pitfalls

- **Always pass a `MatchTimeout`** on user-supplied patterns or input. `RegexMatchTimeoutException` is thrown when exceeded. App-wide default: `AppContext.SetData("REGEX_DEFAULT_MATCH_TIMEOUT", TimeSpan.FromSeconds(2))` before the first regex is created.
- **Catastrophic backtracking** comes from nested quantifiers over overlapping classes (e.g. `(a+)+$`). Mitigations: atomic groups `(?>a+)+`; unroll the loop; `RegexOptions.NonBacktracking` for adversarial input.
- Prefer `(?:…)` (non-capturing) or `RegexOptions.ExplicitCapture` (`n`) when you don't need group captures.
- **Reuse a single `Regex` instance** (or source-generated). Avoid building new `Regex` objects per call.
- Use span-based methods (`IsMatch(ReadOnlySpan<char>)`, `EnumerateMatches`) on hot paths.
- Anchor patterns when validating whole strings: `^…$`. Prefer `\A…\z` to avoid `\n` edge cases.
- Use `Regex.Escape` for culture-derived strings interpolated into a pattern.

```csharp
var r = new Regex(@"^[A-Z]{2,3}-\d{4}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
try { var m = r.Match(input); }
catch (RegexMatchTimeoutException) { /* reject */ }
```
