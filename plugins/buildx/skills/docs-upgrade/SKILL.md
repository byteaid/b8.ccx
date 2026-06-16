---
name: docs-upgrade
description: Slash-command procedure that conforms a repo's EXISTING documentation to the current `development-documentation` canon — classify (legacy / bloated / new-format-with-gaps) → migrate the legacy monolith → decompose over-budget docs → audit gaps (missing GLOSSARY / DATA-MODEL, features without flows, flows without data flows or `## Test` blocks, broken ID cross-references, history debris) → complement each gap from existing material, asking the user whenever a decision is not derivable → verify cross-references and the desired-state invariant. Docs-only — never edits production code or tests. Refuses when NO documentation exists at all and points the user to `/docs-reverse` instead.
when_to_use: |
  - User-invocable only (`/docs-upgrade`): a repo-wide docs migration / completion pass the user triggers deliberately — never auto-invoked.
  - Invoke when: docs predate the current canon (monolithic REQUIREMENT/FLOWS/ARCHITECTURE, PROGRESS/CHANGELOG/ASSESSMENT/archive debris); the hierarchical tree exists but is incomplete (flows without `dataflows/`, missing GLOSSARY.md / DATA-MODEL.md, empty or stale `## Test` FQNs, broken ID cross-references); a desired-state doc is over the compactness budget; history sections sneaked into state docs.
  - NOT for repos with no documentation at all — that is `/docs-reverse`.
user-invocable: true
disable-model-invocation: true
allowed-tools: Bash, Edit, Glob, Grep, PowerShell, Read, Write
---

# /docs-upgrade — conform and complete an existing documentation set

Deterministic procedure. The authoritative rules live in `development-documentation` (bootstrap classification, one leaf per canonical doc, ID taxonomy, desired-state invariant, compactness budget) — this skill only sequences the work and the gates. Read the relevant leaf before authoring or repairing each doc; do not re-derive shapes from memory.

**Scope invariant: docs-only.** Production code and test files are never edited. Code is *read* (to derive data flows and confirm behaviour) but never written. Test work (FQN assignment, new or renamed tests) is recorded as follow-up rows, never executed here.

**Consent note.** Typing `/docs-upgrade` IS the explicit "yes" the `legacy-docs` and `bloated-docs` bootstrap gates require — do not re-ask permission to migrate or decompose. Every *deletion* and every *gap resolution* still passes through the tables below; nothing is deleted or invented silently.

**Dispatch note.** When the `Agent` tool is available (running under `buildx` as the session agent), route REQUIREMENT / GLOSSARY / DATA-MODEL / feature / flow work through `analyst` and SOLUTION / data-flow work through `dotnet-architect` (or the per-stack architect). When it is not (plain session), execute inline following the leaves — same steps, same gates.

## Phase 0 — Classify and inventory (read-only)

1. Run the classification from `development-documentation` § bootstrap § Classification — mechanical, no exceptions.
2. **No documentation at all** (variant `a` or `c` — no `docs/`, or only a `README.md` stub): STOP. Do not create anything. Reply verbatim:

   > This repo has no documentation to upgrade — `/docs-upgrade` starts from an existing `docs/` set (legacy or current format). To reconstruct documentation from the code itself, run `/docs-reverse` (optionally passing a source or output path for migration scenarios).

3. Otherwise inventory: list every file under `docs/`, tag each as `legacy-shape` / `new-format` / `debris` (PROGRESS, CHANGELOG, ASSESSMENT, CODE_INSPECTION, `archive/`), and emit the classification + inventory block.
4. A steady-state classification does NOT end the command — proceed to Phase 3 anyway; the gap audit is the point. Report "no gaps" if it comes back clean.

## Phase 1 — Migrate the legacy monolith (only when `legacy-docs`)

Execute the migration playbook from `development-documentation` § bootstrap § Variant `legacy-docs`, steps 1–9: group FRs into features, split flows out of the monolithic FLOWS.md (one route = one flow, new sequential IDs when a legacy FL packed several), rewrite REQUIREMENT.md to the new shape, create GLOSSARY.md and DATA-MODEL.md, fold ARCHITECTURE.md into SOLUTION.md, derive data flows from the code that realises each migrated flow (describe what IS, not a redesign), delete the legacy files and history debris, move open BACKLOG/BUGS items to `${OS_TEMP}/aix-todo/{repo-basename}/`.

Adjustments versus the bootstrap playbook:

- **Step 6b (derive data flows) requires code.** If the repo has no code yet, do not guess pipelines — record each missing `DF-NNN` as an `ask-user` or `follow-up` row in the Phase 3 gap table instead.
- **Step 10 (test-designer pass) is out of scope.** Record one follow-up row per flow whose `## Test` FQN is empty.

## Phase 2 — Decompose over-budget docs (only when `bloated-docs`)

Execute the decomposition playbook from `development-documentation` § bootstrap § Variant `bloated-docs`, steps 1–6: classify leaked sections, move them to their rightful auxiliary (`feature.md` / `FL-*.md` / `DF-*.md` / referenced sub-doc), rewrite the top-level doc as an index, re-verify IDs, run the desired-state invariant, never drop information in the move.

## Phase 3 — Gap audit (read-only; produces the gap table)

Sweep the (now new-format) tree:

1. **Missing top-level docs** — `GLOSSARY.md`, `DATA-MODEL.md`, `SOLUTION.md`; SOLUTION cost figures missing their `as of YYYY-MM-DD` date.
2. **Tree gaps** — feature folders with no `flows/`; flows with no `dataflows/` (every implementable `FL-NNN` needs 1..N `DF-NNN`); flows missing the `## Test` block or carrying an empty/stale FQN (report-only → follow-up).
3. **Broken cross-references** — grep every cited `FR-NNN` / `NFR-NNN` / `FT-NNN` / `FL-NNN` / `DF-NNN` and confirm it resolves in its owning doc; orphan IDs and duplicate numbers.
4. **Desired-state invariant violations** — history sections, "previously…" prose, `(superseded by …)` annotations, work-in-progress claims, greyed-out diagram nodes (run the checklist from `development-documentation` § Desired-state invariant on every doc).
5. **Vocabulary gaps** — domain terms used in REQUIREMENT/features/flows but absent from GLOSSARY.md; entities referenced but missing from DATA-MODEL.md.
6. **Language** — non-English doc content (docs are English regardless of conversation language).

Emit the **gap table** — one row per finding: `# | Gap | Where | Resolution (derive / ask-user / follow-up) | Notes`. Classification of the resolution column:

- `derive` — fillable from existing docs and/or the code, no judgement call.
- `ask-user` — requires intent the material does not carry (business meaning of a term, whether a behaviour is a requirement or an accident, which feature owns a flow).
- `follow-up` — out of this command's scope (test FQNs, code changes) → a `BL-NNN` row in the temp backlog.

Present the table and **STOP for user approval** before any Phase 4 write.

## Phase 4 — Complement

1. Resolve every `derive` row: author or repair the doc per its `development-documentation` leaf (requirement / glossary / data-model / feature / flow / data-flow / solution). New files follow canonical names and ID sequences exactly.
2. Resolve every `ask-user` row: batch the questions per document (one round per doc, not one question per gap), never default, never invent intent. Record the answer in the doc — the doc is the buffer.
3. Data flows: when code exists, read the code behind the flow and record the pipeline that IS (entry point, numbered steps on specific infrastructure, terminal state, error paths). When no code exists, the data flow records the *designed* pipeline only if the user (or SOLUTION.md) supplies the infrastructure decisions — otherwise it stays an `ask-user` row.
4. Write the `follow-up` rows into `${OS_TEMP}/aix-todo/{repo-basename}/backlog.md` (`BL-NNN`, next free integer); create the temp folder files if missing per `development-documentation` § folder-layout.

## Phase 5 — Verify and hand off

1. Re-run the Phase 0 classification — it MUST come back steady state (new format, all top-level docs within the ≤ ~400-line budget).
2. Re-run the Phase 3 sweeps — zero `derive` rows may remain; surviving rows are only the approved `ask-user`-declined and `follow-up` ones.
3. Run the desired-state invariant checklist on every touched file.
4. Hand off: the gap table with final per-row status, files created / modified / deleted, questions asked and the recorded answers, `BL-NNN` follow-ups opened. Recommend committing with a message that explains what moved where; commit only if the user asks.

## Constraints

- **Docs-only** — production code and tests are never edited; `## Test` FQN authoring belongs to the test-designer (follow-up rows).
- **Never invent intent** — a gap whose answer is not derivable goes to the user, not into a guess.
- **Never silently delete** — legacy-file deletions trace to the Phase 1 playbook; everything else traces to an approved gap-table row.
- **Stop on ambiguity** — a doc that fits neither the legacy nor the new shape goes back to the user.
- **Zero docs → refuse** and point to `/docs-reverse` (Phase 0 verbatim message).

## Cross-references

- `development-documentation` — the canon this skill conforms docs to (§ bootstrap, § Desired-state invariant, and the per-doc leaves).
- `docs-reverse` — the companion skill for repos with no documentation at all.
- Sibling agents (when dispatching): `analyst`, `dotnet-architect`.
