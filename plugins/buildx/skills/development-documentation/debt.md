# DEBT — `${OS_TEMP}/aix-todo/{repo-basename}/debt.md`

## Purpose

`debt.md` is the project's living register of **rule violations and deliberate non-conformances** that the team has chosen to live with — for now. Every entry here is a finding that broke an explicit rule from `dotnet-conventions` (or another conventions skill), was NOT corrected during the slice that produced it, and is therefore carried forward as **technical debt** with an explicit owner, severity, and a recorded reason for survival.

The defining trait: a debt row exists because the rule applies in principle but **either** the fix is out of scope for the current slice (rule applies, fix deferred) **or** the project as a whole is not yet apt for the rule (rule applies at slice-level but the wider codebase is non-conformant; full migration was not requested by the user).

## Location

`${OS_TEMP}/aix-todo/{repo-basename}/debt.md`. Same location as `backlog.md` and `bugs.md`. **NOT git-tracked.** Churns continuously; closed rows are deleted (the trace lives in `git log` via the commit that closed the violation).

## Owner

The **`dotnet-reviewer`** role (per-stack reviewer agent) is the sole writer. The orchestrator (`buildx`) may delete a row when it closes a remediation slice. No other agent writes to this file.

Items can be discovered by anyone (a developer who notices something while editing, the user during conversation, a test-designer during an orphan sweep) — but the row is added only by the reviewer after it has been classified per § Severity and § Status discriminator below.

## ID

`DT-NNN` — three-digit, zero-padded, stable for the life of the project. Reused never. See [id-taxonomy.md](id-taxonomy.md).

## Shape

```markdown
# Debt

> Live register of carried rule violations and deliberate non-conformances. NOT git-tracked. Closed rows are deleted (trace survives in `git log`).

## Open

| ID | Rule | Severity | Status | Where | Owner | First seen | Reason carried | Linked |
|---|---|---|---|---|---|---|---|---|
| DT-001 | `no-test-only-adaptations` | blocker | active | `src/Acme.Web/Program.cs:42` (`if (env.IsTest)` branch) | `dotnet-developer` (current) | 2026-05-24 | flow `FL-014` was authored against this branch; needs test-designer to rewrite test before branch can be removed | FL-014, BG-021 |
| DT-002 | `no-static-bypass-of-di` | major | active | `src/Acme.Web/Services/EmailSender.cs` (`Static.Send(...)`) | `dotnet-developer` (current) | 2026-05-24 | DI motor not in use across the project; user did not request a migration. Slice-level rule survives at project-level non-conformance | (project-wide) |
| DT-003 | `architecture-deviation-hexagonal` | structural | accepted | whole `src/Legacy.Reports/` tree | (no one) | 2026-05-24 | legacy project pre-dating hexagonal adoption; user explicitly accepted leaving it as-is during bootstrap c1 | (project-wide) |
```

The table is the only authoritative shape; do not add free-text sections between rows.

## Columns

| Column | Required | Notes |
|---|---|---|
| `ID` | yes | `DT-NNN`. Stable. Never reused. |
| `Rule` | yes | The exact rule slug from `dotnet-conventions` (e.g., `no-test-only-adaptations`, `no-static-bypass-of-di`, `no-duplicate-or-ambiguous-models`, `architecture-deviation-hexagonal`, `english-only`, `no-hardcoded-secrets`, `try-catch-must-do-work`). One rule per row — if a single file violates two rules, that is two rows. |
| `Severity` | yes | `blocker` / `major` / `minor` / `structural`. See § Severity. |
| `Status` | yes | `active` / `accepted` / `superseded` / `slated`. See § Status discriminator. |
| `Where` | yes | File path + line range if local; `(project-wide)` if it is a global non-conformance. Use `:NNN` line numbers when the row points at a single site; use a glob when it points at many. |
| `Owner` | yes | The agent / role / person responsible for clearing it. `(no one)` is legal for `accepted` rows. |
| `First seen` | yes | ISO date (`YYYY-MM-DD`) the row was added. |
| `Reason carried` | yes | One sentence stating WHY the violation survives this slice. References to the original conversation are not allowed — the reason must read standalone. |
| `Linked` | only if applicable | Comma-separated `FL-NNN` / `FT-NNN` / `BL-NNN` / `BG-NNN` / other `DT-NNN` references that bind the debt to other entries. |

## Severity

| Level | Meaning | Examples |
|---|---|---|
| `blocker` | The violation actively blocks new development OR risks data loss / security incidents. MUST be cleared before the next slice in the same area. | Hardcoded secret in a config file; a test-only adaptation that masks a broken happy path; an authentication bypass left in source. |
| `major` | The violation does not block, but every slice that touches the affected code MUST clear it as part of the slice's clean-as-you-touch (per `dotnet-conventions` § build-quality/clean-as-you-touch). | Static singleton bypassing DI in a class the team edits regularly; duplicated DTO with subtle field divergence. |
| `minor` | Cosmetic / non-functional. Cleared opportunistically. | Spanish identifier in a private helper; comment in mixed language; redundant try/catch that swallows nothing meaningful. |
| `structural` | The violation is at project-level scale — the whole codebase or a whole subtree breaks the rule. The user has NOT requested a migration. Recorded so the team knows the conformance gap exists, but no per-slice action is required. | Whole solution lacks DI motor; whole legacy subtree predates hexagonal; large portion of the codebase still uses `DateTime.UtcNow`. |

## Status discriminator

The status answers: *given this violation exists, what is the team doing about it?*

| Status | When | Reviewer behaviour |
|---|---|---|
| `active` | The violation exists now and will be cleared by the named owner. Slice-scoped or project-scoped, but the team intends to fix it. | New row created; severity assigned; row stays until closed by a commit that removes the violation. |
| `accepted` | The user explicitly authorised the violation to remain (e.g., legacy subtree, vendor-imposed pattern, structural non-conformance the user does not want migrated). The row exists for visibility only. | New row created; severity is usually `structural` or `minor`; owner is `(no one)`; row is deleted only if the surrounding code is deleted. |
| `superseded` | The row was created against a rule version that has since changed; the current rule no longer flags this. | Reviewer deletes the row immediately. Do not keep "superseded" rows around — `git log` is the trace. |
| `slated` | A `BL-NNN` or `BG-NNN` has been created to clear it; the operational queue now owns the clearance. | Row links to the `BL-NNN` / `BG-NNN`; when that piece closes, the orchestrator deletes the row. |

## The aptness rule

A reviewer applies rules **at slice scope by default**. Some rules are only meaningful when the surrounding project is **apt** for them. If the project as a whole does not follow the rule, the reviewer:

1. Flags the slice-level violation IF the change is producing a *new* violation (the developer added a new static bypass — that gets a row).
2. Does NOT request a project-wide migration unless the user has explicitly asked for it.
3. Records a single `structural` row capturing the project-level conformance gap (so the gap is visible to future readers).

Concretely: if the project has no DI motor anywhere, a brand-new `static EmailSender` added by the developer is *still* a debt row (`major`, slice-scoped, status `active`, owner = developer) AND there is *also* a `structural` row noting "project lacks DI motor end-to-end; not slated for migration" (severity `structural`, status `accepted`, owner `(no one)`). The two rows coexist because they describe two different debts: one created by this slice, one inherited.

## Lifecycle

- **Created** when the reviewer opens a finding it does not auto-fix and the developer cannot clear in the same slice.
- **Updated** only by the reviewer (severity downgrade after partial mitigation; `Reason carried` rewrite if context shifts; status change `active → slated` once a `BL-NNN` exists).
- **Closed** by `buildx`: when the commit that clears the violation lands, `buildx` deletes the row in the same slice that closes the linked `BL-NNN` / `BG-NNN`. `accepted` rows close only if their referenced code is deleted.
- **Never archived.** No "Closed" section, no "DT history". The `git log` of the source file that carried the violation is the historical record.

## Discoverability

- The reviewer lists every open `DT-NNN` row in its hand-off after every dispatch.
- The orchestrator reads `debt.md` at session start to know what blocker rows exist; a `blocker` row gates new slices in the affected area.
- The user can ask the orchestrator for a debt snapshot any time; the orchestrator surfaces the `debt.md` content verbatim.

## Audit checklist (reviewer runs every save)

- [ ] Every row has all required columns populated.
- [ ] No row references a `BL-NNN` / `BG-NNN` / `FL-NNN` that does not exist.
- [ ] No `superseded` row survives — they are deleted on sight.
- [ ] `Severity` matches the actual impact (no `minor` row for a hardcoded production secret).
- [ ] Project-level non-conformance is captured as exactly ONE `structural` row, not duplicated per slice.

## See also

- [skill.md](skill.md) — master table of canonical docs.
- [id-taxonomy.md](id-taxonomy.md) — `DT-NNN` prefix definition.
- [backlog.md](backlog.md), [bugs.md](bugs.md) — operational queue siblings.
- `dotnet-conventions` § forbidden-patterns — the rule catalog whose slugs appear in the `Rule` column.
- `dotnet-conventions` § build-quality/clean-as-you-touch — the scope-bounded policy that turns `major` debt into a slice action.
