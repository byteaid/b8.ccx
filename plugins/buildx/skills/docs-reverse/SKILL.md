---
name: docs-reverse
description: Slash-command procedure that reverse-engineers a codebase into the canonical `development-documentation` set — map the code (read-only) → derive REQUIREMENT (FR/NFR) → GLOSSARY → DATA-MODEL → features → flows → SOLUTION → data flows, asking the user at every ambiguous behaviour instead of inventing intent. An optional path argument enables migration mode: a path holding code = the SOURCE to reverse (docs land in the current repo, e.g. the first step of an Angular→Blazor rebuild); an empty or nonexistent path = the OUTPUT folder for the docs. In migration mode the stack-agnostic docs transfer as-is, while SOLUTION.md and the data flows are NOT derived from the source stack — they belong to the target architect.
when_to_use: |
  - User-invocable only (`/docs-reverse [path]`): a repo-wide reverse-engineering pass the user triggers deliberately — never auto-invoked.
  - Invoke when: code exists but `docs/` does not (bootstrap variant c1 on demand); a stack migration starts and the old codebase must be distilled into tech-agnostic docs that drive the new build; docs must be seeded into a separate output folder for a future repo.
  - NOT for repos that already carry canonical or legacy docs — that is `/docs-upgrade`.
user-invocable: true
disable-model-invocation: true
allowed-tools: Bash, Glob, Grep, PowerShell, Read, Write
---

# /docs-reverse — reconstruct documentation from code

Deterministic procedure. The authoritative shapes live in `development-documentation` (one leaf per canonical doc, ID taxonomy, desired-state invariant) and the playbook mirrors `development-documentation` § bootstrap § Variant `existing-code-greenfield-docs` — this skill makes it user-invocable, docs-only, and migration-aware. Read the relevant leaf before authoring each doc; do not re-derive shapes from memory.

**Prime directive: describe what IS.** The derived docs record the behaviour, vocabulary, and pipelines the code actually implements — never a target redesign, never an invented intent. Every ambiguous behaviour STOPS and asks the user before being recorded as an `FR-NNN`.

**Dispatch note.** When the `Agent` tool is available (running under `buildx` as the session agent), route REQUIREMENT / GLOSSARY / DATA-MODEL / feature / flow derivation through `analyst` and SOLUTION / data-flow derivation through `dotnet-architect` (or the per-stack architect). When it is not (plain session), execute inline following the leaves — same steps, same gates.

## Argument — mode resolution (run first)

`/docs-reverse` accepts zero or one path argument. Resolve the mode mechanically:

| Argument | Mode | Source (code read) | Output (docs written) |
|---|---|---|---|
| none | **in-place** | current repo | `./docs/` of the current repo |
| path exists AND contains source code | **migration** | the argument path | `./docs/` of the current repo |
| path does not exist, or exists empty | **seed-output** | current repo | the argument path (created) |
| path exists with neither code nor an empty tree | ambiguous | — | **ask the user** which role the path plays |

Guards, before any derivation:

1. If the **output** location already carries canonical new-format docs (`REQUIREMENT.md` + `features/`) or legacy-format docs (monolithic REQUIREMENT/FLOWS/ARCHITECTURE), STOP and point to `/docs-upgrade` — this skill never overwrites or reconciles an existing doc set.
2. If the **source** location has no recognisable code, STOP and report what was found — there is nothing to reverse.
3. State the resolved mode, source, and output explicitly before Phase 1 and let the user veto.

**Migration mode semantics.** The point of reversing a codebase that is about to be replaced (Angular→Blazor, monolith→services) is to capture the *stack-agnostic* desired state: requirements, vocabulary, data model, features, flows. Those transfer to the new stack verbatim. What does NOT transfer: `SOLUTION.md` (the old stack's infrastructure is not the target's) and the `DF-NNN` data flows (pipelines name specific infrastructure) — both are authored later by the target architect. Phase 6 and 7 are skipped in migration mode and recorded as follow-ups.

## Phase 1 — Map the code (read-only)

Enumerate top-level modules, public surfaces, entry points (HTTP routes, UI screens/pages, CLI verbs, message/queue handlers, timers), build files, deployment manifests, runtime config. Do not edit anything. Emit a map block: module → surfaces → entry points; this map drives every derivation below and the final coverage check.

## Phase 2 — Derive REQUIREMENT

Extract observable behaviours into `FR-NNN` (and operational constraints into `NFR-NNN`) per `development-documentation` § requirement. **Stop at every ambiguous behaviour and ask the user before recording** — batch questions per module, never default, never invent intent from code shape alone. Distinguish behaviour from accident: an undocumented quirk goes to the user as "requirement or bug?", and a confirmed bug becomes a `BG-NNN` row in the temp queue, not an FR.

## Phase 3 — Derive GLOSSARY

From code identifiers (entity / value-object / enum names) and the FRs just written, extract every domain term into `GLOSSARY.md` per `development-documentation` § glossary. Confirm with the user any term whose business meaning is not obvious from code shape. Source identifiers in another language are translated — the doc term is English, the original identifier is noted in the entry so the mapping survives.

## Phase 4 — Derive DATA-MODEL

From type definitions, ORM models, schemas, and DTOs, build `DATA-MODEL.md` (entities, value objects, enums, ER diagram, invariants) per `development-documentation` § data-model. Conceptual only — persistence specifics stay out (they go to SOLUTION.md in in-place mode, or to the target architect in migration mode).

## Phase 5 — Derive features and flows

1. Group FRs into `FT-NNN` features; create `features/FT-NNN-{kebab}/feature.md` per `development-documentation` § feature.
2. From every entry point in the Phase 1 map, produce one or more `flows/FL-NNN-{kebab}.md` — one route = one flow, per `development-documentation` § flow. Each flow carries the `## Test` block with an **empty FQN** (the test-designer fills it later; test authoring is out of scope).
3. Coverage check: every entry point in the map traces to at least one flow, and every flow traces back to an entry point. Unmatched entries go to the user, not into a guess.

## Phase 6 — Derive SOLUTION (in-place and seed-output modes only)

From the dependency graph, deployment manifests, and runtime config, name the infrastructure / apps / communication that exist, per `development-documentation` § solution. Cost figures: only if the user provides them — never guess; date everything provided. **Skipped in migration mode** — record a follow-up: "author SOLUTION.md for the target stack".

## Phase 7 — Derive data flows (in-place and seed-output modes only)

Read the code behind each flow and record the pipeline that IS — entry point, numbered steps on the specific infrastructure each touches (every component resolving to a SOLUTION.md row), terminal state, error paths — as 1..N `dataflows/DF-NNN-{kebab}.md` per `development-documentation` § data-flow. **Skipped in migration mode** — record a follow-up: "derive DF-NNN per flow once the target SOLUTION.md exists".

## Phase 8 — Verify and hand off

1. Grep the output tree: every cited `FR-NNN` / `NFR-NNN` / `FT-NNN` / `FL-NNN` / `DF-NNN` resolves; no duplicate IDs.
2. Run the `development-documentation` § Desired-state invariant checklist on every produced file, including the ≤ ~400-line compactness budget.
3. Seed the operational queue in `${OS_TEMP}/aix-todo/{repo-basename}/` (`backlog.md`, `bugs.md`, `debt.md`) with the follow-ups and confirmed bugs collected along the way.
4. Hand off: the Phase 1 map with per-entry coverage status, files produced, questions asked and answers recorded, follow-ups opened. Recommended next steps by mode — **in-place**: Aspire enrolment + test-designer pass per `development-documentation` § bootstrap § c1; **migration**: target SOLUTION.md, then data flows, then plan iteration 1 of the rebuild; **seed-output**: move the folder into the future repo and run `/docs-upgrade` there to re-verify.

## Constraints

- **Read-only on code** — the source codebase is never modified, in any mode.
- **Never invent FRs from code shape** without user confirmation; ambiguity always goes to the user.
- **Describe the system that exists**, not a proposed redesign — redesign belongs to the target architect after this skill completes.
- **Never overwrite an existing doc set** — guard 1 routes to `/docs-upgrade`.
- **English output** regardless of the source code's identifier language; original terms preserved in GLOSSARY entries.

## Cross-references

- `development-documentation` — the canon (§ bootstrap § `existing-code-greenfield-docs`, § Desired-state invariant, and the per-doc leaves).
- `docs-upgrade` — the companion skill when documentation already exists (legacy or current format).
- Sibling agents (when dispatching): `analyst`, `dotnet-architect`.
