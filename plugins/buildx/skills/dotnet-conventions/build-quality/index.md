# .NET conventions — Build quality

Zero-warnings rule, clean-as-you-touch policy, `dotnet build` as the hard gate before delivery, three-attempts-then-search, canonical handback format.

## Final topics

| Trigger | File |
|---|---|
| Zero-errors, zero-warnings rule (`dotnet build` exit is the gate) | [zero-warnings-rule.md](zero-warnings-rule.md) |
| Clean-as-you-touch — what to eradicate in a file you're already editing | [clean-as-you-touch.md](clean-as-you-touch.md) |
| 3-attempts-then-search — when to stop iterating locally and search official sources | [three-attempts-then-search.md](three-attempts-then-search.md) |
| Reporting back — canonical handback format | [handback-format.md](handback-format.md) |

> Project documentation (CHANGELOG, REQUIREMENT, FLOWS, ARCHITECTURE, PROGRESS, BACKLOG, BUGS, todo, ASSESSMENT, CODE_INSPECTION, test-cycle reports) lives in `development-documentation`. Load that skill when authoring or updating any project doc, including CHANGELOG.md.

## See also

- [../forbidden-patterns/index.md](../forbidden-patterns/index.md) — the "eradicate on sight" list is the backbone of clean-as-you-touch
- `development-documentation` § changelog — CHANGELOG discipline (was previously a leaf here)
