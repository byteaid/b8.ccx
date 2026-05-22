# Backlog document — `${OS_TEMP}/aix-todo/{repo-basename}/backlog.md`

## Purpose

backlog.md answers: *what feature-level work is waiting to be picked up?* It is the orchestrator's pending queue. Each entry is a `BL-NNN` with enough detail (title, value, effort, dependency) for prioritisation without re-deriving the whole picture.

## Owner

The **orchestrator** role produces and maintains backlog.md. The user feeds new items by stating them; the orchestrator records, prioritises, and processes. Worker roles never edit backlog.md.

## Where

- Path: `${OS_TEMP}/aix-todo/{repo-basename}/backlog.md` (operational queue, outside the repo).
- Tracked: **no** (not git-tracked).
- Lifecycle: open items only. **Closed items are deleted** — the trace lives in `git log` (the commits that landed the deliverables). No "Done" section, no past-iteration blocks, no archive.

## What goes in

- One table of open `BL-NNN` items.
- For each item: `BL-NNN`, title, value statement (one line), effort estimate, dependency (other `BL-NNN` or `FR-NNN` / `FT-NNN`).

## What does NOT go in

- Closed items. They are deleted on close.
- Defects — those live in [bugs.md](bugs.md).
- Per-task decomposition of the in-progress item — that lives in [todo.md](todo.md).
- "Cancelled" or "Wontfix" sections — if the user drops an item, delete it; the commit that closed it (or the conversation log) is the record.

## Format

```markdown
# Backlog

> Items currently open. Closed items are deleted; the trace lives in git log.

| ID     | Title                                       | Value                              | Effort | Depends on |
|--------|---------------------------------------------|------------------------------------|--------|------------|
| BL-014 | Export reports to PDF                       | High — sales team needs offline    | M      | FR-012     |
| BL-015 | Bulk-import customers from CSV              | Medium — onboarding speed          | L      | BL-014     |
| BL-016 | Two-factor enrolment for operators          | High — security policy             | M      | —          |
```

## Lifecycle

- **Created** at project bootstrap as a stub with an empty table.
- **Updated** when:
  - the user adds a new feature → new `BL-NNN` row appended;
  - the orchestrator picks a piece → the row stays (work-in-flight state is implicit; the orchestrator knows from the active todo.md);
  - a piece closes → the row is **deleted**.
- **State is implicit.** No `Pending` / `InProgress` / `Done` column. Either the item is in the file (open) or it is not (closed or never existed). The orchestrator tracks "currently in flight" via the live `todo.md`.
- **Closed when** the operational queue is wiped (rare — typically at major project transitions). The file lives as long as the project does.

## Rules

- **One canonical backlog.md per project** at `${OS_TEMP}/aix-todo/{repo-basename}/backlog.md`. Do NOT create per-area backlogs.
- **Effort scale is project-defined** (e.g., S / M / L / XL). State the scale once at the top of the file if needed; otherwise the orchestrator uses a consistent informal scale.
- **No "Closed" or "Past iterations" sections.** When closing an item, delete it.
- **Cross-references must resolve.** Cited `FR-NNN` / `FT-NNN` must exist in `docs/REQUIREMENT.md` / `docs/features/`.

## IDs

- `BL-NNN` — backlog items; counter is local to this file. Numbering does NOT reset on item close (`BL-014` is gone forever once closed; the next item is `BL-017`).
- `FR-NNN` / `FT-NNN` referenced in `Depends on`; defined in [requirement.md](requirement.md) and [feature.md](feature.md).

## See also

- [bugs.md](bugs.md) — the defect queue (separate axis).
- [todo.md](todo.md) — the per-iteration decomposition of the picked piece.
- [requirement.md](requirement.md) — the FRs each backlog item realises.
