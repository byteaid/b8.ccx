# ID taxonomy — cross-cutting

## Purpose

Every claim in a project document that ties to a requirement, a feature, a flow, a task, a backlog item, or a bug carries a stable identifier. The ID is the unit of cross-document traceability — without it, "the order flow" becomes ambiguous across documents. With it, every reference resolves to exactly one entry in exactly one file.

## Rule

| Prefix | Meaning | Lives in | Format |
|---|---|---|---|
| `FR-NNN` | Functional requirement | [requirement.md](requirement.md) | `FR-NNN. {one-line title}` followed by description, acceptance bullets, business rules |
| `NFR-NNN` | Non-functional requirement | [requirement.md](requirement.md) | same shape; constraint axis (latency, availability, security, accessibility, i18n, observability) |
| `FT-NNN` | Feature | [feature.md](feature.md) — one file per FT at `docs/features/FT-NNN-{kebab}/feature.md` | `# FT-NNN — {Feature title}` heading + body |
| `FL-NNN` | Flow (a single user route through a feature) | [flow.md](flow.md) — one file per FL at `docs/features/FT-NNN-{kebab}/flows/FL-NNN-{kebab}.md` | `# FL-NNN — {route title}` heading + body with mandatory `## Test` section |
| `T-NNN` | Task in the current iteration | [todo.md](todo.md) (temp) | row in a block-of-10 task table |
| `BL-NNN` | Backlog item (feature request) | [backlog.md](backlog.md) (temp) | row in open table; deleted on close |
| `BG-NNN` | Bug | [bugs.md](bugs.md) (temp) | row in open table; deleted on close |
| `DT-NNN` | Technical-debt entry — a carried rule violation or accepted non-conformance | [debt.md](debt.md) (temp) | row with Rule / Severity / Status / Where / Owner / Reason carried; deleted on close |

## Retired prefixes (do NOT use)

These were used in earlier versions of this skill and are no longer valid:

- `D-NNN` — Decisions log. **Retired in v0.4.0.** Desired-state docs no longer carry a Decisions log; the reason for any change lives in the commit message and `git log`.
- `G-NNN`, `R-NNN`, `F-NNN`, `B-NNN`, `XX-NNN` — Assessment / inspection / test-cycle / blocker / inspector codes. **Retired in v0.4.0** along with the docs that owned them (`ASSESSMENT.md`, `CODE_INSPECTION.md`, test-cycle `REPORT.md`, `PROGRESS.md`).

If a legacy doc references a retired prefix, the migration playbook in [bootstrap.md](bootstrap.md) § Variant `legacy-docs` handles its retirement.

## Examples

```
FR-001. Place order
NFR-003. P95 latency under 200 ms for the catalog endpoint
FT-001 — Checkout
FL-007 — Place order, payment succeeds
FL-008 — Place order, payment declines
T-005. Add IReportExporter contract + skeleton
BL-014. Export reports to PDF
BG-009. Cancel order does not recompute total
DT-003. Static singleton bypassing DI in `EmailSender` — slice-scope active, severity major
```

## Cross-references

A reference uses the full prefix and is read as a hyperlink even when it isn't one:

- "BG-014 affects FL-007 — fixing requires the test at `Company.Product.Test.Http.Orders_Tests.CancelOrder_RecomputesTotal` to pass."
- "FT-001 covers FR-001 and FR-002, expressed as flows FL-001 through FL-008."
- "BL-022 introduces FT-005, which the planner will decompose into T-NNN entries."

## Rules

- **IDs are stable.** Once issued, an ID is never reused, never renumbered. Removing a feature deletes its folder and frees the number for **no one** — the next FT gets the next free integer.
- **Zero-padded to 3 digits by default.** `FR-001`, `FR-042`. Bump to 4 digits only if a project genuinely exceeds 999 entries in a category.
- **Locally unique within prefix, globally.** Two `FR-007` in the same project is a bug. The same number across prefixes is fine: `FR-007`, `FT-007`, `FL-007`, `BG-007` may coexist.
- **`FL-NNN` numbering is global** across the entire project, not per feature. `FT-001` may contain `FL-001`..`FL-007`; `FT-002` may then start at `FL-008`. The flow file lives under its owning feature's folder, but its number is drawn from a single global counter.
- **`FT-NNN` folder name is `FT-NNN-{kebab}`.** The kebab suffix is a human-friendly slug derived from the feature title (`login`, `checkout`, `report-export`). Renaming the slug is a documentation rename; the `FT-NNN` itself stays stable.
- **`FL-NNN-{kebab}.md` follows the same shape.** The slug is the route, not the feature (`login-success`, `login-wrong-password`, `login-locked-account`).
- **Never invent an ID retroactively.** If a cited ID doesn't exist, fix the upstream document first — do not paper over with a fake number.
- **A "title" line is not optional.** `FR-007. {title}` reads at a glance; bare `FR-007` followed by prose is harder to scan.

## Enforcement

- The handback report (owned by the per-stack conventions skill — e.g. `dotnet-conventions` § build-quality/handback-format for .NET) lists which IDs were touched per delivery. Reviewers grep the cited IDs back to the docs to confirm they resolve.
- An ID that is referenced from another document but not defined in its owner is broken — treat as a documentation defect, not a content question.
- New ID prefixes are not allowed without amending this leaf. If a project genuinely needs a new prefix, propose it via a backlog item first.

## See also

- [folder-layout.md](folder-layout.md) — where each document lives.
- [skill.md](skill.md) — overview of the canonical doc set.
- [flow.md](flow.md) — the mandatory `## Test` block where the FQN of the realising test is recorded.
