# Feature document — `docs/features/FT-NNN-{kebab}/feature.md`

## Purpose

A feature document answers: *what is this feature, what user problem does it solve, which functional requirements does it realise, and which flows compose it?* It is the intermediate doc between the global `REQUIREMENT.md` and the per-route flow files.

It is read by: the analyst (to keep the feature aligned with its FRs), the architect (to know whether the feature needs a new app/canal in `SOLUTION.md`), the test-designer (to scope the per-flow tests).

## Owner

The analyst role produces and maintains every `feature.md`. The test-designer role does NOT write `feature.md` — its responsibility is the `## Test` block inside each `FL-NNN-*.md` under the feature's `flows/` folder.

## Where

- Path: `docs/features/FT-NNN-{kebab}/feature.md`, where `FT-NNN` is the feature's stable ID (zero-padded 3 digits) and `{kebab}` is a slug derived from the feature title (`login`, `checkout`, `report-export`).
- Tracked: yes (git).
- Lifecycle: desired state — **rewritten in place** when the feature evolves. No history sections, no Decisions log.

## What goes in

- One-line feature summary.
- Goal: which user problem this feature solves.
- FR coverage: the list of `FR-NNN` (and optionally `NFR-NNN`) this feature realises.
- Actors involved.
- Flow index: every `FL-NNN-{kebab}.md` under `flows/`, with a one-line description of each route.
- Optional: a small diagram showing the relationships between flows (e.g., a state diagram of an entity that the feature manipulates).
- Out-of-scope items at the feature level.
- Open questions specific to this feature.

## What does NOT go in

- Per-route step-by-step description — that lives inside each `FL-NNN-{kebab}.md`.
- Per-pipeline data design — that lives in the architect's `dataflows/DF-NNN-{kebab}.md` files (see [data-flow.md](data-flow.md)); `feature.md` carries no data-flow index (data flows cite their parent `FL-NNN` themselves).
- Test FQNs — those live in the `## Test` block of each flow.
- Implementation details, framework choices, endpoints — [solution.md](solution.md).
- Cross-cutting requirements that apply to the whole product — [requirement.md](requirement.md).

## Format

```markdown
# FT-NNN — {Feature title}

> One-line summary.

## Goal

{The user problem this feature solves and the outcome it produces.}

## FR coverage

| ID     | Title                     |
|--------|---------------------------|
| FR-001 | Authenticate a user       |
| FR-002 | Lock accounts after 5 fails |

## Actors

| Actor    | Role in this feature                |
|----------|--------------------------------------|
| User     | Submits credentials                 |
| System   | Validates and issues a session token |

## Flows

| ID     | Route                              | File                                   |
|--------|------------------------------------|----------------------------------------|
| FL-001 | Login with correct credentials      | `flows/FL-001-login-success.md`         |
| FL-002 | Login with wrong password           | `flows/FL-002-login-wrong-password.md`  |
| FL-003 | Login with locked account           | `flows/FL-003-login-locked-account.md`  |

## Out of scope

- {Explicit non-goal of this feature}

## Open questions

- [ ] {Unresolved question specific to this feature}
```

## Lifecycle

- **Created** when a new feature is identified — the analyst picks the next free `FT-NNN`, creates the folder `docs/features/FT-NNN-{kebab}/`, writes `feature.md`, and adds at least one flow under `flows/`.
- **Updated in place** when the feature evolves. Re-add / remove flows in the Flow index when flows are added / split / removed.
- **Renamed:** the `{kebab}` suffix may be renamed when the feature title changes; the `FT-NNN` stays the same. The folder rename is a single commit. Update any cross-references in `REQUIREMENT.md`, other feature files, and tests.
- **Deleted** when the feature is retired. Delete the whole folder (including `flows/` and `dataflows/`). Open `BL-NNN` / `BG-NNN` referencing the feature must be closed or re-targeted first.

## Rules

- **One `feature.md` per `FT-NNN`.** Never split a single feature across two folders.
- **The Flow index is mandatory.** A feature without flows is unverifiable — at least one `FL-NNN` must live under `flows/`.
- **FRs cited here MUST exist** in `REQUIREMENT.md`. Cross-references that do not resolve are documentation defects.
- **Folder slug is kebab-case, ASCII, lowercase.** No spaces, no underscores, no uppercase.
- **No `## Test` block here.** Tests live in the per-flow files. The feature is verified by the union of its flow tests.

## See also

- [requirement.md](requirement.md) — the global FRs this feature realises.
- [flow.md](flow.md) — the per-route doc each feature contains.
- [data-flow.md](data-flow.md) — the architect-owned `dataflows/` subfolder each feature carries.
- [id-taxonomy.md](id-taxonomy.md) — the `FT-NNN` and `FL-NNN` numbering rules.
