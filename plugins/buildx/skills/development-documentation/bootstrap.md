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
| New-format `docs/` present, but a top-level desired-state doc (`REQUIREMENT.md` / `GLOSSARY.md` / `DATA-MODEL.md` / `SOLUTION.md`) is over the ≤ ~400-line compactness budget (SKILL § hard rule 10) — detail that belongs in `feature.md` / `FL-*.md` / a sub-doc has leaked up | **bloated-docs** — new format but not decomposed (block until decomposed) |
| Code present AND new-format `docs/` present, coherent, AND every top-level desired-state doc is within the compactness budget | steady state — skip bootstrap          |

Edge cases:
- `docs/REMOTE-SYNC.md` is the **optional integration ledger** (SKILL § hard rule 12), not a state doc and not debris — **ignore it during classification**. It never triggers `legacy-docs` or `bloated-docs`, and its absence is normal (most repos are not externalized). Bootstrap seeds it **only** when the user opts into externalization; otherwise it does not exist. See [remote-sync.md](remote-sync.md).
- `docs/` partial AND code partial → treat as **c**; the existing partial docs feed the reverse-engineering pass but do not exempt the project from the sub-variant choice.
- Multi-module repo with code in some modules and not others → still one bootstrap, one `docs/` at the repo root.
- If BOTH old-format docs AND new-format `docs/features/` are present, treat as **legacy-docs**: the project is mid-migration and must be reconciled before any other work.

## Variant a — empty

Working directory is blank. The user describes what they want; the conversation refines it into REQUIREMENT.md + at least one feature + at least one flow + SOLUTION.md **before any code is written**.

### Playbook

1. **Elicit the product idea.** One open question first ("what are you building, and for whom?"); then drill into goals, primary user, success criteria. Never assume a stack.
2. **Refine into requirements.** Capture `FR-NNN` / `NFR-NNN` as they crystallise; write them into `docs/REQUIREMENT.md` as you go (the doc IS the buffer). See [requirement.md](requirement.md).
3. **Seed the glossary.** Extract every domain term the user used during elicitation into `docs/GLOSSARY.md` — one entry per concept. See [glossary.md](glossary.md). Subsequent steps must reference these terms verbatim.
4. **Draft the data model.** From the glossary entities, write `docs/DATA-MODEL.md`: entities, value objects, enums, ER diagram, invariants. Conceptual only — no storage choices. See [data-model.md](data-model.md).
5. **Identify features.** Group related FRs into `FT-NNN` features. For each feature create `docs/features/FT-NNN-{kebab}/feature.md`. See [feature.md](feature.md).
6. **Sketch flows.** For each feature draft at least one `FL-NNN-{kebab}.md` under `flows/`. Each flow is one route. See [flow.md](flow.md). Tests are not written yet but the `## Test` block exists with a tentative FQN.
7. **Propose the solution.** Pick infrastructure / apps / communication / vendors; record in `docs/SOLUTION.md` with dated cost figures. See [solution.md](solution.md).
8. **Design the data flows.** The architect derives 1..N `DF-NNN-{kebab}.md` per flow under each feature's `dataflows/` folder: entry point, step-by-step pipeline, specific infrastructure (every component resolving to a SOLUTION.md row). See [data-flow.md](data-flow.md). No flow enters implementation without its data flows.
9. **Seed the operational queue.** Create `${OS_TEMP}/aix-todo/{repo-basename}/{backlog.md,bugs.md,debt.md}` empty. `todo.md` is born when the first iteration plans.
10. **Plan iteration 1.** The architect writes the live `todo.md` with the first slice of `BL-NNN` to deliver. See [todo.md](todo.md).
11. **Write tests.** The test-designer creates the per-flow real tests; updates each `FL-NNN-*.md` `## Test` FQN. See per-stack test-designer skill.
12. **Implement.** The developer makes the tests pass, implementing exactly the pipelines the `DF-NNN` files specify.
13. **Review.** The reviewer scans the implementation and registers any rule violations into `debt.md` — including the code↔data-flow mapping check. See [debt.md](debt.md) and per-stack reviewer agent.

### Stop conditions

- REQUIREMENT.md, GLOSSARY.md, DATA-MODEL.md, and at least one feature with one flow and its data flow(s) exist, IDs cross-link cleanly, SOLUTION.md is dated.
- Coding does NOT start before this point. If the user pushes earlier, surface that the design IDs are not stable yet and ask explicitly.

## Variant b — documentation without code

Working directory has `docs/` in the new hierarchical shape but no implementation. Bootstrap is **conform-and-refine, then plan iteration 1**.

### Playbook

1. **Inventory.** List every file under `docs/`. Confirm structure (`REQUIREMENT.md` + `GLOSSARY.md` + `DATA-MODEL.md` + `SOLUTION.md` + `features/FT-*/feature.md` + `.../flows/FL-*-*.md` + `.../dataflows/DF-*-*.md`).
2. **Compliance pass.** For each canonical doc, open it, check structure against its leaf rules, repair drift (missing IDs, broken cross-references, stale dates).
3. **Gap report.** List every L1 doc that is missing OR below threshold (e.g. a feature folder with no flows; a flow with no data flow; a missing `GLOSSARY.md` / `DATA-MODEL.md`; a glossary entry referenced in REQUIREMENT.md that has no definition).
4. **Refine in place.** With the user, drive each gap to closure — same elicitation moves as variant **a**, starting from the existing material instead of blank. Per the default behaviour, **`GLOSSARY.md` and `DATA-MODEL.md` MUST exist after this step**; if they are missing, the analyst seeds them now (this is a hard requirement for variant b on any repo bootstrapped under v0.5.0 or later). Flows lacking data flows get them from the architect (per [data-flow.md](data-flow.md)) before any implementation is sequenced.
5. **ID consolidation.** Renumber if a prefix has duplicates; otherwise leave IDs stable. Run a grep across `docs/` to confirm every cited ID resolves.
6. **Seed operational queue** in temp (`backlog.md`, `bugs.md`, `debt.md` — `todo.md` is born when iteration 1 plans).
7. **Plan iteration 1, write tests, implement, review** — same as variant a steps 10–13.

## Variant c — code without (or with insufficient) documentation

Code exists; the canonical doc set is missing. **The user MUST choose** one of three sub-variants before any work begins. Ask explicitly; never default.

### Choice prompt (verbatim shape)

> Your project has code but the documentation is missing. Pick one:
>
> - **c1** — Full reverse engineering. I will read the codebase and produce the complete hierarchical `docs/` set (REQUIREMENT.md + GLOSSARY.md + DATA-MODEL.md + features + flows + SOLUTION.md), asking you for clarification on every ambiguous decision before I record it. Then the test-designer writes the per-flow tests, the developer ensures they pass, and the reviewer registers any carried debt into `debt.md`.
> - **c2** — Minimal docs. I will create only the operational files in `${OS_TEMP}/aix-todo/{repo-basename}/` to control the current request. Nothing under `docs/`.
> - **c3** — No docs. I will proceed straight to code without writing any documentation.

Wait for the user's choice. Do not pre-select.

### Sub-variant c1 — full reverse-engineered docs

Equivalent to the **`existing-code-greenfield-docs`** flow described below. See that section for the full procedure. The user can also trigger this derivation directly (docs-only, migration-aware) via the user-invocable `docs-reverse` skill.

### Sub-variant c2 — minimal docs

Create exactly four files in `${OS_TEMP}/aix-todo/{repo-basename}/`:

- `backlog.md` — the request the user is about to drive.
- `bugs.md` — even if empty, so defects discovered during the work have a place to land.
- `debt.md` — even if empty, so rule violations the reviewer finds have a place to land.
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

Wait for explicit `yes`. Do not default. (When the user themselves invoked the `docs-upgrade` skill, that invocation IS the explicit yes — proceed without re-asking.)

### Migration playbook (executed by `analyst` in `migrate` mode)

1. **Read** the legacy `docs/REQUIREMENT.md`, `docs/FLOWS.md`, `docs/ARCHITECTURE.md`.
2. **Group FRs into features.** Cluster requirements by user-facing concern (login, checkout, reporting, admin). Each cluster becomes an `FT-NNN` with the next free integer. Slug = kebab of the feature title.
3. **For each feature**, create `docs/features/FT-NNN-{kebab}/feature.md` with the FRs it covers.
4. **For each `FL-NNN` in the legacy `docs/FLOWS.md`**, decide which feature owns it. Create `docs/features/FT-NNN-{kebab}/flows/FL-NNN-{kebab}.md` with the route description. Add the `## Test` block with an empty FQN field (the test-designer fills it later). **If a single legacy FL-NNN packed multiple routes, split it into N flows with new sequential IDs.**
5. **Rewrite `docs/REQUIREMENT.md`** to the new shape: FR/NFR + Feature index table. Strip Decisions log if present.
5b. **Create `docs/GLOSSARY.md`** by extracting every domain term referenced in REQUIREMENT.md and the new feature/flow set. Confirm contested terms with the user before recording.
5c. **Create `docs/DATA-MODEL.md`** from the entity/value-object/enum names referenced in REQUIREMENT.md and (if applicable) the structural section of legacy `ARCHITECTURE.md`. Storage specifics from ARCHITECTURE.md go to SOLUTION.md, not here.
6. **Fold `docs/ARCHITECTURE.md` into `docs/SOLUTION.md`** (executed by `dotnet-architect` after analyst completes the desired-state pass). The architecture content (components, communication, data model, environment strategy) joins the existing SOLUTION sections.
6b. **Derive data flows** (executed by `dotnet-architect` in the same migration). For each migrated flow, read the code that realises it and record the existing pipeline as 1..N `DF-NNN-{kebab}.md` under the owning feature's `dataflows/` folder per [data-flow.md](data-flow.md) — describe what IS, not a redesign.
7. **Delete** `docs/FLOWS.md`, `docs/ARCHITECTURE.md`, `docs/PROGRESS.md`, `docs/CHANGELOG.md`, `docs/ASSESSMENT.md`, `docs/CODE_INSPECTION.md`, `docs/archive/`. Strip Decisions logs from all surviving state docs.
8. **Move (or create)** `BACKLOG.md` and `BUGS.md` to `${OS_TEMP}/aix-todo/{repo-basename}/` as `backlog.md` and `bugs.md`. Open items only; closed items are dropped (the trace lives in `git log`). Create an empty `debt.md` in the same folder — the reviewer will populate it after step 10.
9. **Commit** the migration in one or more commits with messages explaining the new structure.
10. **Test-designer pass.** Once migration completes, the test-designer is dispatched to assign real FQNs to each flow's `## Test` block, possibly creating new test classes.

After step 10, the repo is in steady state.

## Variant `bloated-docs` — new format, but a desired-state doc is not decomposed

The repo is already in the hierarchical format (`docs/features/` exists), but at least one top-level desired-state doc has grown past the ≤ ~400-line compactness budget (SKILL § hard rule 10). The usual cause: per-feature / per-flow detail, or a SOLUTION deep-dive, was inlined upward instead of pushed into its auxiliary. Every planning / review / reconciliation dispatch then re-reads the bloated file, so the cost compounds on every cycle.

### Hard rule — no work proceeds until decomposed

This is a **mandatory** decomposition, blocking exactly like `legacy-docs`. The orchestrator MUST refuse to process any `BL-NNN` / `BG-NNN` / user request until it completes. The exact message:

> One of your desired-state docs is over the compactness budget ({file} — {N} lines). The team's format keeps each top-level doc a lean index and pushes detail down into `features/FT-*/feature.md`, `flows/FL-*.md`, or a referenced sub-doc. I cannot work on this repo until that doc is decomposed. Accept decomposing?
>
> - **yes** — I will run the decomposition now and proceed.
> - **no** — I will stop. The doc stays bloated and I will not process this request.

Wait for explicit `yes`. Do not default.

### Decomposition playbook (the owner of each bloated doc executes; orchestrator routes)

1. **Identify the leaked content.** For each over-budget doc, classify every section as either *belongs here* (the doc's own concern per its leaf) or *leaked* (per-feature, per-flow, or deep-dive detail).
2. **Move leaked content to its rightful auxiliary.** Per-feature prose → the owning `docs/features/FT-*/feature.md`. Per-flow prose → the owning `flows/FL-*.md`. A per-route pipeline treatment inside `SOLUTION.md` → the owning `dataflows/DF-*.md`. A SOLUTION deep-dive → a referenced sub-doc (e.g. `docs/solution/AUTH-DESIGN.md`) linked from `SOLUTION.md`. Create the auxiliary if it does not exist; never drop information in the move.
3. **Rewrite the top-level doc as an index.** What remains is its own concern plus cross-links down (`REQUIREMENT.md` = FR/NFR + feature index; `SOLUTION.md` = apps/comms/infra/cost + links to sub-docs). Confirm it is now within budget.
4. **Re-verify IDs and cross-references** — grep `docs/` so every cited `FR-NNN` / `FT-NNN` / `FL-NNN` still resolves after the move.
5. **Run the desired-state invariant** (SKILL § Desired-state invariant) on every touched file, including the new compactness box.
6. **Commit** the decomposition with a message explaining what moved where.

Ownership: `analyst` decomposes `REQUIREMENT.md` / `GLOSSARY.md` / `DATA-MODEL.md` / `feature.md` / `FL-*.md`; the architect decomposes `SOLUTION.md` (including relocating leaked pipeline detail into `dataflows/DF-*.md`). After this completes, the repo is in steady state.

## Variant `existing-code-greenfield-docs` — code exists, no docs, user picked full reverse

Code is present, no `docs/` exists, the user picked sub-variant `c1`. The procedure:

### Playbook

1. **Map the code.** Use search to enumerate top-level modules, public surfaces, build files. Do not edit code in this phase.
2. **Derive REQUIREMENT.** Extract observable behaviours into `FR-NNN`. **Stop at every ambiguous behaviour and ask the user before recording.** Do not invent intent from code shape alone.
3. **Derive GLOSSARY.** From code identifiers (entity / value-object / enum names) and the FRs just written, extract every domain term into `docs/GLOSSARY.md`. Confirm with the user any term whose business meaning is not obvious from code shape.
4. **Derive DATA-MODEL.** From the type definitions, ORM models, and DTOs, build `docs/DATA-MODEL.md` (entities, value objects, enums, ER diagram, invariants). Persistence specifics stay out — they go to SOLUTION.md.
5. **Derive features.** Group FRs into `FT-NNN`. Create `docs/features/FT-NNN-{kebab}/feature.md` for each.
6. **Derive flows.** From every entry point (HTTP route, CLI verb, UI screen, message handler), produce one or more `FL-NNN-{kebab}.md`. The `## Test` FQN is filled in step 10.
7. **Derive SOLUTION.** From the dependency graph, deployment manifests, and runtime config, name infrastructure / apps / communication. Cost figures: skip unless the user provides them; do not guess.
8. **Derive data flows.** The architect reads the code behind each flow and records the pipeline as it actually exists — entry point, step-by-step transformations, the specific infrastructure each step touches (every component resolving to a SOLUTION.md row) — as 1..N `DF-NNN-{kebab}.md` under the owning feature's `dataflows/` folder. Describe the pipeline that IS, not a target redesign. See [data-flow.md](data-flow.md).
9. **Enrol in Aspire** (per-stack — for .NET this means scaffolding `Company.Product.AppHost` and wiring all discovered projects). Owned by the architect via `dotnet-aspire` (or the matching per-stack skill).
10. **Write tests.** The test-designer creates the per-flow real tests (Playwright / HTTP / CLI / gRPC); updates each `FL-NNN-*.md` `## Test` FQN.
11. **Seed the operational queue** in temp (`backlog.md`, `bugs.md`, `debt.md`; `todo.md` is born when iteration 1 plans).
12. **Initial review pass.** The reviewer scans the existing code against the conventions and registers every carried rule violation into `debt.md` — typically a `structural` row per project-level non-conformance the user explicitly accepted during reverse-engineering (no DI, partial hexagonal, etc.).

Hard rule: **never modify production code during steps 1–9**. The pass is read-only on the codebase. Tests in step 10 are additive and must not require any production-code change. The review in step 12 only writes `debt.md`; it does not touch code.

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
new format but a desired-state doc over budget            → BLOCK; offer decomposition → run on yes
docs + code coherent AND all top-level docs within budget → steady state
```

## Enforcement

- The classification is mechanical — every bootstrap starts with the scan in § Classification.
- The c-variant choice is **always asked, never defaulted**.
- `legacy-docs` is a **hard block** — no other work can proceed until the user accepts migration.
- `bloated-docs` is a **hard block** too — the compactness check (SKILL § hard rule 10) runs on every bootstrap scan, including on otherwise-steady-state repos; a top-level desired-state doc over the ≤ ~400-line budget reclassifies the repo to `bloated-docs` and blocks until the user accepts decomposition.
- Bootstrap touches `docs/` and the temp folder only. Code changes belong to the iteration that follows.

## See also

- [skill.md](skill.md) — the master table of canonical docs the bootstrap produces.
- [folder-layout.md](folder-layout.md) — where the files land and what is git-tracked.
- [id-taxonomy.md](id-taxonomy.md) — the ID prefixes the bootstrap seeds across the docs.
- [requirement.md](requirement.md), [feature.md](feature.md), [flow.md](flow.md), [data-flow.md](data-flow.md), [solution.md](solution.md) — the desired-state docs.
- [todo.md](todo.md), [backlog.md](backlog.md), [bugs.md](bugs.md) — the operational queue.
