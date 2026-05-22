# Three-attempts-then-search

## Rule

If you fail to resolve a specific symptom **three times** with the same root-cause hypothesis, STOP iterating locally. Search official sources for the exact error / API / behavior. After a second failed search, escalate to a senior consultant with the full trail.

The team has documented incidents of 1.5+ hours burned on a TLS handshake error before someone read the log. The fix is usually two searches away.

## The ladder

### Attempt 1 — Re-read

- Re-read your own change. Verify assumptions about the API surface, the resource name, the type signature.
- Read the failing test / build error literally — what is it actually saying?
- Check the obvious: typo, wrong parameter, swapped arguments, missing `await`.

### Attempt 2 — Read the local skill

- If the problem is Aspire-related: open the relevant section of `dotnet-aspire`.
- If it's CLI / tooling: open the matching skill (`dotnet-system-commandline`, `dotnet-file-based-apps`).
- If it's a team rule: check this skill's index for a forbidden-pattern leaf or a positive-rule leaf.
- The skill is the cheapest source — use it before the open web.

### Attempt 3 — Web-search the EXACT error

- STOP iterating. Open a web-search tool.
- Search the **literal error message** in quotes, plus the API/method name. Example: `"Pre-Login handshake error" "TrustServerCertificate"`.
- Restrict to **official sources first**: `learn.microsoft.com`, `github.com/dotnet/*`, `aspire.dev`, `playwright.dev`, `devblogs.microsoft.com`, RFC tracker, the package's own GitHub issues.
- Date-bound to the last 12 months — the .NET / Aspire stack moves fast and stale answers misdirect.

When you find a fix:

- **Apply it.**
- **Cite the source** in a one-line code comment so the next contributor can verify (`// see https://github.com/dotnet/aspire/issues/12345`).
- **Report the search** in your handback (problem, three attempts, source URL, fix).

### Two-strikes escalation

If the search-driven fix also fails (you are now at attempts 4 and 5 without resolution):

- **Stop.** A third identical retry is forbidden — it burns context for no learning.
- **Escalate** with the full trail: the original symptom, the three local attempts, the search results, the applied fix, and why it didn't work.
- The orchestrator routes to a senior consultant role for diagnosis. The consultant returns a Markdown analysis; the orchestrator routes the recommended fix.

## What counts as "the same root-cause hypothesis"

If attempt 1 fails on hypothesis A, attempt 2 on hypothesis B, attempt 3 on hypothesis C — those are three **different** attempts and you may try once more on a fourth hypothesis without escalating yet.

If attempts 1–3 all hypothesize "missing connection string" and all fail with the same "CS null" symptom — that's the same root cause, three strikes, time to search.

## Anti-patterns

- **Spinning.** Trying the same fix with one tweaked parameter five times.
- **Over-searching.** Going to the web before reading the local skill — wastes the cheap source.
- **Hidden retries.** Wrapping a failing call in a try/retry loop to "make it stop failing" instead of finding the cause.
- **Silencing.** Catching and ignoring the exception. Worst possible response — see also [../forbidden-patterns/no-warning-suppression.md](../forbidden-patterns/no-warning-suppression.md).

## Enforcement

- **Handback contract:** if a problem cost more than three iterations, the report cites the source and the fix.
- **Two-strikes review:** a recurring failure with no escalation trail is a process violation.

## See also

- [handback-format.md](handback-format.md) — the report shape that surfaces escalations.
