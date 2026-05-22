# Todo document — `${OS_TEMP}/aix-todo/{repo-basename}/todo.md`

## Purpose

todo.md answers: *what concrete tasks make up the piece in flight, in what order, and who owns each?* It is the per-iteration plan — a single piece (`BL-NNN` feature, `BG-NNN` bug, or compliance objective) decomposed into tasks of one-deliverable / one-owner each, organised in blocks of 10. The orchestrator walks this file task by task; the file is never read out of order.

## Owner

The **architect** role produces and maintains `todo.md` (this responsibility moved from the retired `planner` role in v0.4.0). The orchestrator never writes to it; worker roles never write to it. Re-plans mid-iteration are produced by re-invoking the architect, never by an in-place edit by another role.

## Where

todo.md is one of the three operational docs that live **outside the repo**:

- **Live path:** `${OS_TEMP}/aix-todo/{repo-basename}/todo.md`
  - `${OS_TEMP}` resolves to `$env:TEMP` on Windows and `${TMPDIR:-/tmp}` on POSIX.
  - `{repo-basename}` = basename of the repo's working directory at bootstrap time (deterministic per project).
- **Tracked:** **no** (not git-tracked — the file is regenerated per iteration and is not part of the repository's history).
- **Archive policy:** none. When the iteration closes, the file is overwritten by the next iteration's plan. The audit trail lives in `git log` via the commits that landed each task's deliverable.
- **Discoverability:** the orchestrator and every implementer agent are told the live path explicitly in their brief — they do NOT search for it.

## What goes in

- Header: piece ID (`BL-NNN` / `BG-NNN`), planned-at timestamp, total tasks, total blocks, status.
- Rationale: 3–8 lines explaining why this decomposition.
- Constraints: cross-doc IDs the plan must respect (`FR-NNN`, `FT-NNN`, `FL-NNN`).
- Block 1 …: a table per block (ID / Owner / Title / Depends on / Deliverable / Skill citation). Tasks are `T-001 … T-010`, then `T-011 … T-020`, etc.
- Block-closure checkpoints: what scoped verification each block ends with (typically: "run the tests added/touched in this block").
- Replan log: append-only entries when the plan was revised mid-iteration.
- Notes for the orchestrator: anything non-obvious about parallelism, sequencing, external dependencies.

## What does NOT go in

- Backlog items not in flight — those live in `backlog.md`.
- Acceptance criteria — those live in [requirement.md](requirement.md). todo.md tasks are *deliverables*, not *assertions*.
- The result / status of each task — git log records what landed; the live todo.md is the plan, not the journal.

## Format

```markdown
# Iteration plan — {timestamp}

## Header

- **Piece:** BL-014 (Export reports to PDF)
- **Planned at:** 2026-05-16 09:00
- **Total tasks:** 14
- **Total blocks:** 2
- **Status:** planning-complete

## Rationale

The export pipeline is decomposed test-first: block 1 writes the failing acceptance test (test-designer), block 2 implements the production code (developer) until it passes. Block 2 closes on a scoped verification (`dotnet test` filter on the export-related class).

## Constraints

- Must respect FR-012 acceptance criteria; FL-007 (Export PDF — happy path) is the user flow.
- Deadline: 2026-05-25.

## Block 1 — T-001 … T-010

| ID    | Owner                | Title                                        | Depends on | Deliverable                                            | Skill citation                |
|-------|----------------------|----------------------------------------------|------------|--------------------------------------------------------|--------------------------------|
| T-001 | dotnet-test-designer | Real HTTP test for export endpoint           | —          | Failing test `Reports_Tests.ExportPdf_*_ReturnsPdf`    | dotnet-test-designer skill     |
| T-002 | dotnet-developer     | IReportExporter contract + skeleton          | T-001      | Interface + skeleton class                              | dotnet-hexagonal-architecture |
| T-003 | dotnet-developer     | Wire export endpoint                          | T-002      | Endpoint method + route                                 | —                              |
| T-004 | dotnet-developer     | PDF rendering adapter                         | T-002      | Adapter implementing IReportExporter                    | —                              |
| T-005 | dotnet-test-designer | Negative tests (auth, validation)             | T-003      | Two failing tests added                                 | dotnet-test-designer skill     |
| T-006 | dotnet-developer     | Validation + auth wiring                      | T-005      | Endpoint passes negative tests                          | —                              |
| T-007 | dotnet-developer     | File-name strategy + headers                  | T-004      | Endpoint sets correct filename + content-type           | —                              |
| T-008 | dotnet-developer     | Audit-log entry on export                     | T-003      | Audit row written on each export                         | —                              |
| T-009 | dotnet-test-designer | Audit-log assertion                           | T-008      | Test asserts the row                                    | dotnet-test-designer skill     |
| T-010 | dotnet-developer     | Run full `dotnet test` filter on Reports area | T-001..T-009 | All Reports tests PASS                                 | —                              |

## Block 2 — T-011 … T-014

| ID    | Owner                | Title                                | Depends on | Deliverable                                  | Skill citation                |
|-------|----------------------|--------------------------------------|------------|----------------------------------------------|--------------------------------|
| T-011 | dotnet-test-designer | UI test for export happy path        | T-007      | Failing Playwright test                       | playwright-dotnet              |
| T-012 | dotnet-developer     | Export button + progress indicator   | T-011      | UI control wired to the endpoint              | —                              |
| T-013 | dotnet-developer     | Empty / error states for export      | T-012      | UI handles 4xx / 5xx                          | —                              |
| T-014 | dotnet-developer     | Run full `dotnet test`               | T-011..T-013 | All tests PASS                              | —                              |

## Block-closure checkpoints

- After block 1: `dotnet test --filter "FullyQualifiedName~Reports_Tests"` GREEN.
- After block 2: full `dotnet test` GREEN.

## Replan log

- (empty)

## Notes for the orchestrator

- T-007 and T-008 can run in parallel after T-003 lands.
- The PDF rendering adapter (T-004) likely needs a sandbox key — surface to user before starting.
```

## Lifecycle

- **Created** when the orchestrator picks a piece and invokes the architect to plan it.
- **Updated** only via re-planning. Mid-iteration scope changes follow the loop: orchestrator detects drift → REQUIREMENT.md / features / SOLUTION.md update → architect re-invoked → todo.md is rewritten with completed tasks preserved at the top, remaining blocks regenerated, and a Replan log entry appended.
- **Block size is exactly 10 tasks** (the last block may be shorter). The last task of every block is a scoped verification (typically: run the tests added/touched in this block).
- **One task = one owner = one deliverable.** A row that lists two owners must be split.
- **Skill citations are optional but valuable.** When the architect knows which sub-skill the worker should load, citing it shaves one round-trip.
- **Closed when** the iteration closes. The architect overwrites the file at the next iteration's planning. No archive is kept; `git log` of the affected source files is the trace.

## IDs

- `T-NNN` — tasks; counter is local to this iteration's file. Numbering resets per iteration.
- `BL-NNN` / `BG-NNN` — referenced as the piece; defined in `backlog.md` / `bugs.md`.

## See also

- [backlog.md](backlog.md) / [bugs.md](bugs.md) — the source of the piece being planned.
- [feature.md](feature.md), [flow.md](flow.md) — the desired-state docs the tasks must respect.
- [solution.md](solution.md) — the components / wiring the tasks must respect.
