# Bootstrap — initializing or migrating the docs of any project

## Purpose

Bring any project — whether it has nothing, has documentation only, has code only, or has documentation in the old monolithic format — to a state where the canonical desired-state docs (or the deliberately reduced subset) are correct, hierarchical, internally consistent, and traceable. Bootstrap is the **entry-point procedure** every other doc leaf assumes has already happened.

The procedure is deterministic: scan the working directory, classify it into one of the variants, then follow the matching playbook. **Variant `c` always asks the user to pick a sub-variant**; **variant `legacy-docs` blocks all other work until the user accepts migration**.

## Classification (run first, no exceptions)

Walk the repo root and decide which variant applies:

| Signal                                                                                                                | Variant                                |
|-----------------------------------------------------------------------------------------------------------------------|----------------------------------------|
| Empty folder, or only `.git` / IDE noise / a `README.md` stub                                                          | **a** — empty                          |
| `docs/` exists with at least one file in the new hierarchical shape (`REQUIREMENT.md` + `features/FT-*/feature.md`), no code under `src/` | **b** — docs without code              |
| Code present (any stack), `docs/` missing                                                                              | **c** — code without docs (ask sub-variant) |
| `docs/REQUIREMENT.md` OR `docs/FLOWS.md` (monolithic) present, OR `docs/ARCHITECTURE.md` present, OR `docs/PROGRESS.md` / `CHANGELOG.md` / `ASSESSMENT.md` / `CODE_INSPECTION.md` present, OR `docs/archive/` present, AND `docs/features/` is missing | **legacy-docs** — old format detected (block until migrated) |
| Code present AND new-format `docs/` present and coherent                                                               | steady state — skip bootstrap          |

Edge cases:
- `docs/` partial AND code partial → treat as **c**; the existing partial docs feed the reverse-engineering pass but do not exempt the project from the sub-variant choice.
- Multi-module repo with code in some modules and not others → still one bootstrap, one `docs/` at the repo root.
- If BOTH old-format docs AND new-format `docs/features/` are present, treat as **legacy-docs**: the project is mid-migration and must be reconciled before any other work.

## Variant a — empty

Working directory is blank. The user describes what they want; the conversation refines it into REQUIREMENT.md + at least one feature + at least one flow + SOLUTION.md **before any code is written**.

### Playbook

1. **Elicit the product idea.** One open question first ("what are you building, and for whom?"); then drill into goals, primary user, success criteria. Never assume a stack.
2. **Refine into requirements.** Capture `FR-NNN` / `NFR-NNN` as they crystallise; write them into `docs/REQUIREMENT.md` as you go (the doc IS the buffer). See [requirement.md](requirement.md).
3. **Identify features.** Group related FRs into `FT-NNN` features. For each feature create `docs/features/FT-NNN-{kebab}/feature.md`. See [feature.md](feature.md).
4. **Sketch flows.** For each feature draft at least one `FL-NNN-{kebab}.md` under `flows/`. Each flow is one route. See [flow.md](flow.md). Tests are not written yet but the `## Test` block exists with a tentative FQN.
5. **Propose the solution.** Pick infrastructure / apps / communication / vendors; record in `docs/SOLUTION.md` with dated cost figures. See [solution.md](solution.md).
6. **Seed the operational queue.** Create `${OS_TEMP}/aix-todo/{repo-basename}/{backlog.md,bugs.md}` empty. `todo.md` is born when the first iteration plans.
7. **Plan iteration 1.** The architect writes the live `todo.md` with the first slice of `BL-NNN` to deliver. See [todo.md](todo.md).
8. **Write tests.** The test-designer creates the per-flow real tests; updates each `FL-NNN-*.md` `## Test` FQN. See per-stack test-designer skill.
9. **Implement.** The developer makes the tests pass.

### Stop conditions

- REQUIREMENT.md and at least one feature with one flow exist, IDs cross-link cleanly, SOLUTION.md is dated.
- Coding does NOT start before this point. If the user pushes earlier, surface that the design IDs are not stable yet and ask explicitly.

## Variant b — documentation without code

Working directory has `docs/` in the new hierarchical shape but no implementation. Bootstrap is **conform-and-refine, then plan iteration 1**.

### Playbook

1. **Inventory.** List every file under `docs/`. Confirm structure (`REQUIREMENT.md` + `SOLUTION.md` + `features/FT-*/feature.md` + `.../flows/FL-*-*.md`).
2. **Compliance pass.** For each canonical doc, open it, check structure against its leaf rules, repair drift (missing IDs, broken cross-references, stale dates).
3. **Gap report.** List every L1 doc that is missing OR below threshold (e.g. a feature folder with no flows).
4. **Refine in place.** With the user, drive each gap to closure — same elicitation moves as variant **a**, starting from the existing material instead of blank.
5. **ID consolidation.** Renumber if a prefix has duplicates; otherwise leave IDs stable. Run a grep across `docs/` to confirm every cited ID resolves.
6. **Seed operational queue** in temp.
7. **Plan iteration 1, write tests, implement** — same as variant a steps 7–9.

## Variant c — code without (or with insufficient) documentation

Code exists; the canonical doc set is missing. **The user MUST choose** one of three sub-variants before any work begins. Ask explicitly; never default.

### Choice prompt (verbatim shape)

> Your project has code but the documentation is missing. Pick one:
>
> - **c1** — Full reverse engineering. I will read the codebase and produce the complete hierarchical `docs/` set (REQUIREMENT.md + features + flows + SOLUTION.md), asking you for clarification on every ambiguous decision before I record it. Then the test-designer writes the per-flow tests and the developer ensures they pass.
> - **c2** — Minimal docs. I will create only the operational files in `${OS_TEMP}/aix-todo/{repo-basename}/` to control the current request. Nothing under `docs/`.
> - **c3** — No docs. I will proceed straight to code without writing any documentation.

Wait for the user's choice. Do not pre-select.

### Sub-variant c1 — full reverse-engineered docs

Equivalent to the **`existing-code-greenfield-docs`** flow described below. See that section for the full procedure.

### Sub-variant c2 — minimal docs

Create exactly three files in `${OS_TEMP}/aix-todo/{repo-basename}/`:

- `backlog.md` — the request the user is about to drive.
- `bugs.md` — even if empty, so defects discovered during the work have a place to land.
- `todo.md` — written when an iteration is planned.

Skip everything under `docs/`. Do not create empty stubs.

### Sub-variant c3 — no docs

Skip all docs entirely. Proceed directly to the requested code change. Surface the trade-off explicitly so the user understands future audits will see an undocumented change.

If during the work a defect or scope question arises that genuinely needs a written trace, surface it and ask whether to escalate to c2 — never silently start writing docs the user opted out of.

## Variant `legacy-docs` — old format detected

The repo carries documentation in the pre-v0.4.0 monolithic shape: `docs/REQUIREMENT.md` and `docs/FLOWS.md` as single giant files, `docs/ARCHITECTURE.md` separate from `docs/SOLUTION.md`, possibly `docs/PROGRESS.md` / `CHANGELOG.md` / `ASSESSMENT.md` / `CODE_INSPECTION.md` / `docs/archive/`.

### Hard rule — no work proceeds until migrated

The orchestrator MUST refuse to process any `BL-NNN` / `BG-NNN` / user request until the migration completes. The exact message:

> Your repo's documentation is in the legacy monolithic format (REQUIREMENT.md + FLOWS.md + ARCHITECTURE.md, or PROGRESS/CHANGELOG/ASSESSMENT/CODE_INSPECTION). The team's current format is hierarchical (`docs/REQUIREMENT.md` + `docs/features/FT-*/feature.md` + `.../flows/FL-*-*.md` + `docs/SOLUTION.md`). I cannot work on this repo until the docs are migrated. Accept migrating?
>
> - **yes** — I will run the migration now and proceed.
> - **no** — I will stop. The repo stays in the legacy format and I will not process this request.

Wait for explicit `yes`. Do not default.

### Migration playbook (executed by `analyst` in `migrate` mode)

1. **Read** the legacy `docs/REQUIREMENT.md`, `docs/FLOWS.md`, `docs/ARCHITECTURE.md`.
2. **Group FRs into features.** Cluster requirements by user-facing concern (login, checkout, reporting, admin). Each cluster becomes an `FT-NNN` with the next free integer. Slug = kebab of the feature title.
3. **For each feature**, create `docs/features/FT-NNN-{kebab}/feature.md` with the FRs it covers.
4. **For each `FL-NNN` in the legacy `docs/FLOWS.md`**, decide which feature owns it. Create `docs/features/FT-NNN-{kebab}/flows/FL-NNN-{kebab}.md` with the route description. Add the `## Test` block with an empty FQN field (the test-designer fills it later). **If a single legacy FL-NNN packed multiple routes, split it into N flows with new sequential IDs.**
5. **Rewrite `docs/REQUIREMENT.md`** to the new shape: FR/NFR + Feature index table. Strip Decisions log if present.
6. **Fold `docs/ARCHITECTURE.md` into `docs/SOLUTION.md`** (executed by `architect` after analyst completes the desired-state pass). The architecture content (components, communication, data model, environment strategy) joins the existing SOLUTION sections.
7. **Delete** `docs/FLOWS.md`, `docs/ARCHITECTURE.md`, `docs/PROGRESS.md`, `docs/CHANGELOG.md`, `docs/ASSESSMENT.md`, `docs/CODE_INSPECTION.md`, `docs/archive/`. Strip Decisions logs from all surviving state docs.
8. **Move (or create)** `BACKLOG.md` and `BUGS.md` to `${OS_TEMP}/aix-todo/{repo-basename}/` as `backlog.md` and `bugs.md`. Open items only; closed items are dropped (the trace lives in `git log`).
9. **Commit** the migration in one or more commits with messages explaining the new structure.
10. **Test-designer pass.** Once migration completes, the test-designer is dispatched to assign real FQNs to each flow's `## Test` block, possibly creating new test classes.

After step 10, the repo is in steady state.

## Variant `existing-code-greenfield-docs` — code exists, no docs, user picked full reverse

Code is present, no `docs/` exists, the user picked sub-variant `c1`. The procedure:

### Playbook

1. **Map the code.** Use search to enumerate top-level modules, public surfaces, build files. Do not edit code in this phase.
2. **Derive REQUIREMENT.** Extract observable behaviours into `FR-NNN`. **Stop at every ambiguous behaviour and ask the user before recording.** Do not invent intent from code shape alone.
3. **Derive features.** Group FRs into `FT-NNN`. Create `docs/features/FT-NNN-{kebab}/feature.md` for each.
4. **Derive flows.** From every entry point (HTTP route, CLI verb, UI screen, message handler), produce one or more `FL-NNN-{kebab}.md`. The `## Test` FQN is filled in step 7.
5. **Derive SOLUTION.** From the dependency graph, deployment manifests, and runtime config, name infrastructure / apps / communication. Cost figures: skip unless the user provides them; do not guess.
6. **Enrol in Aspire** (per-stack — for .NET this means scaffolding `Company.Product.AppHost` and wiring all discovered projects). Owned by the architect via `dotnet-aspire` (or the matching per-stack skill).
7. **Write tests.** The test-designer creates the per-flow real tests (Playwright / HTTP / CLI / gRPC); updates each `FL-NNN-*.md` `## Test` FQN.
8. **Seed the operational queue** in temp.

Hard rule: **never modify production code during steps 1–6**. The pass is read-only on the codebase. Tests in step 7 are additive and must not require any production-code change.

### Common pitfalls

- Inventing FRs from code shape without user confirmation.
- Proposing a target architecture instead of describing the one that exists.
- Skipping Aspire enrolment "because the project already runs" — without Aspire the test surface is not reachable.

## Decision summary

```
empty                                                    → a
docs only (new format)                                   → b
code only, user picks c1 (= existing-code-greenfield-docs) → full reverse + Aspire + tests
code only, user picks c2                                  → temp files only
code only, user picks c3                                  → no docs
legacy docs detected                                      → BLOCK; offer migration → run on yes
docs + code coherent                                      → steady state
```

## Enforcement

- The classification is mechanical — every bootstrap starts with the scan in § Classification.
- The c-variant choice is **always asked, never defaulted**.
- `legacy-docs` is a **hard block** — no other work can proceed until the user accepts migration.
- Bootstrap touches `docs/` and the temp folder only. Code changes belong to the iteration that follows.

## See also

- [skill.md](skill.md) — the master table of canonical docs the bootstrap produces.
- [folder-layout.md](folder-layout.md) — where the files land and what is git-tracked.
- [id-taxonomy.md](id-taxonomy.md) — the ID prefixes the bootstrap seeds across the docs.
- [requirement.md](requirement.md), [feature.md](feature.md), [flow.md](flow.md), [solution.md](solution.md) — the desired-state docs.
- [todo.md](todo.md), [backlog.md](backlog.md), [bugs.md](bugs.md) — the operational queue.
