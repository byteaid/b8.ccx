---
name: development-documentation
description: Canonical project documentation set every team-managed software project carries. Desired-state docs only — `docs/REQUIREMENT.md`, `docs/SOLUTION.md`, and a hierarchical `docs/features/FT-NNN-{name}/{feature.md, flows/FL-NNN-{name}.md}` tree. Operational docs (`todo.md`, `backlog.md`, `bugs.md`) live OUTSIDE the repo at `${OS_TEMP}/aix-todo/{repo-basename}/`. Greppable IDs (`FR-NNN` / `NFR-NNN` / `FT-NNN` / `FL-NNN` / `T-NNN` / `BL-NNN` / `BG-NNN`). One file per concept, single-owner per file, English, markdown. Tech-agnostic — provider, framework, runtime independent. State docs are PURE desired state — no history sections, no Decisions log, no supersession trails (git log is the historical archive). Load when authoring, updating, auditing, or migrating any project doc.
when_to_use: |
  - Triggers: REQUIREMENT.md, SOLUTION.md, feature.md, flow.md, FR-NNN, NFR-NNN, FT-NNN, FL-NNN, BL-NNN, BG-NNN, T-NNN, todo.md, backlog.md, bugs.md, project documentation, docs folder, ID taxonomy, traceability, doc bootstrap, doc audit, initialize project, new project, blank repo, reverse engineer docs, migrate legacy docs.
  - Tasks: bootstrap a new project's `docs/`; author or update REQUIREMENT, a feature, or a flow; place a new flow under its feature folder; author or update SOLUTION; write the live `todo.md` / `backlog.md` / `bugs.md` in the temp folder; reverse-engineer docs from existing code; migrate a legacy `REQUIREMENT.md` + `FLOWS.md` monolith into the hierarchical `features/` tree; audit a state doc for sneaked-in history.
allowed-tools: Edit, Glob, Grep, NotebookEdit, Read, Write
user-invocable: false
---

# Project documentation

L1 dispatcher. Concrete content lives in flat L2 leaves — one per canonical doc plus three cross-cutting leaves (folder layout, ID taxonomy, bootstrap). Open the leaf that matches the trigger; do not read the whole tree.

## Mental model

A project's documentation is a **living artifact set with canonical names, a single owner per file, and a strict separation between desired state and operational queue.**

- **Desired state lives in `docs/`** and is git-tracked. It describes what the system MUST be: the requirements, the features, every possible flow, the chosen infrastructure. **No history, ever.** Supersession is in-place rewrite. The reason for any change is the commit message, not a Decisions log inside the doc. Git log is the historical archive.
- **Operational queue lives in `${OS_TEMP}/aix-todo/{repo-basename}/`** and is NOT git-tracked. It is the work in flight: tasks for the current iteration (`todo.md`), open backlog items (`backlog.md`), open bugs (`bugs.md`). It churns continuously; closed items are deleted, their trace surviving via commits.

Cross-document traceability is mechanical: `FR-NNN` ↔ `FT-NNN` ↔ `FL-NNN` ↔ `T-NNN` ↔ `BG-NNN`. A claim without an ID is a claim that cannot be cited.

The taxonomy is **technology-agnostic**. The same doc shapes apply to any stack. Stack-specific knowledge (the layout of source modules, the concrete code-inspection codes, the test-runner failure buckets) is owned by per-stack skills that this skill cross-references — never duplicates.

## Hard rules

1. **Canonical names, exact case.** Desired-state docs: `docs/REQUIREMENT.md`, `docs/SOLUTION.md`, `docs/features/FT-NNN-{kebab}/feature.md`, `docs/features/FT-NNN-{kebab}/flows/FL-NNN-{kebab}.md`. Operational docs: `${OS_TEMP}/aix-todo/{repo-basename}/{todo.md, backlog.md, bugs.md}` (lowercase). Greppability across projects depends on this.
2. **One canonical doc per concept.** Sub-docs may be referenced from a canonical doc but never replace it. The L1 set is mandatory; auxiliary `.md` files are optional and cross-linked.
3. **Location.** All desired-state docs live under `docs/` at the repo root. The three operational docs (`todo.md`, `backlog.md`, `bugs.md`) live **outside the repo** at `${OS_TEMP}/aix-todo/{repo-basename}/` because they churn continuously and have no audit value once their items close. See [folder-layout.md](folder-layout.md).
4. **Markdown, English, git-tracked** (for everything under `docs/`). Even when conversational language with the user is something else, the docs themselves stay English.
5. **Every claim that ties to a flow / requirement / bug / task carries an ID.** Never describe "the login flow" — say "FL-014". See [id-taxonomy.md](id-taxonomy.md).
6. **State docs are desired state only.** No "Previous behaviour" sections, no "Old flow" diagrams, no "Decisions log". When a requirement, feature, or flow changes, the doc is **rewritten in place** to reflect the new desired state; the reason and the prior shape live in the commit message and `git log`.
7. **One flow = one test.** Every `FL-NNN-*.md` carries a `## Test` block with the fully-qualified name of exactly one real test (HTTP / UI / gRPC / CLI / etc.). If a single route validates UI + API together, it is one flow and one Playwright end-to-end test. If they are tested separately, they are two flows.
8. **Operational docs are ephemeral.** `todo.md` is rewritten per iteration. `backlog.md` / `bugs.md` items are deleted on close — the trace survives via commits, not via "closed items" sections. No `docs/archive/`. No compaction policy. No threshold checks.
9. **Handback discipline names which docs were touched per delivery** — owned by the per-stack conventions skill (e.g. `dotnet-conventions` § build-quality/handback-format for .NET).

## Master table — canonical docs

| Document | Path | Owner | Lifecycle | Leaf |
|---|---|---|---|---|
| REQUIREMENT | `docs/REQUIREMENT.md` | analyst | desired state; FR/NFR + feature index; rewritten in place | [requirement.md](requirement.md) |
| Feature | `docs/features/FT-NNN-{kebab}/feature.md` | analyst | one per feature; description + FR cross-links; rewritten in place | [feature.md](feature.md) |
| Flow | `docs/features/FT-NNN-{kebab}/flows/FL-NNN-{kebab}.md` | analyst (skeleton + test FQN field), test-designer (fills `## Test` FQN) | one per route; rewritten in place | [flow.md](flow.md) |
| SOLUTION | `docs/SOLUTION.md` | architect | infrastructure + apps + comms + costs (dated); rewritten in place | [solution.md](solution.md) |
| todo | `${OS_TEMP}/aix-todo/{repo-basename}/todo.md` | architect | live per-iteration `T-NNN` list; overwritten per iteration; NOT tracked | [todo.md](todo.md) |
| backlog | `${OS_TEMP}/aix-todo/{repo-basename}/backlog.md` | orchestrator | open `BL-NNN` items only; closed items deleted; NOT tracked | [backlog.md](backlog.md) |
| bugs | `${OS_TEMP}/aix-todo/{repo-basename}/bugs.md` | orchestrator | open `BG-NNN` items only; closed items deleted; NOT tracked | [bugs.md](bugs.md) |

The "owner" column names a *role*. Where a deployed agent exists for a role, it owns the file; otherwise the human or an orchestrator-style agent stewards the file.

## Cross-cutting leaves

| Leaf | Purpose |
|---|---|
| [bootstrap.md](bootstrap.md) | Entry-point procedure: classify the working directory (empty / docs-only / code-only / legacy-docs) and run the matching playbook (variants `a`, `b`, `c1`, `c2`, `c3`, `legacy-docs`, `existing-code-greenfield-docs`). |
| [folder-layout.md](folder-layout.md) | The `docs/` tree shape, the temp folder shape, what is git-tracked vs not. |
| [id-taxonomy.md](id-taxonomy.md) | Every ID prefix (`FR`, `NFR`, `FT`, `FL`, `T`, `BL`, `BG`), where it lives, how it crosses documents. |

## Desired-state invariant (apply on every state-doc write)

Before saving REQUIREMENT.md, any `feature.md`, any `FL-NNN-*.md`, or SOLUTION.md, run this checklist. The doc PASSES only when all boxes are ticked.

- [ ] No section titled "History", "Previous", "Old", "Legacy", "Decisions log", "Changelog".
- [ ] No prose like "It used to be …" / "We previously had …" / "Before vX, this …".
- [ ] No `(superseded by FR-NNN)` annotations — superseded entries are rewritten in place or deleted; the prior text is removed.
- [ ] Cost / version / date numbers are dated (`as of YYYY-MM-DD`) so a stale figure is recognisable.
- [ ] Mermaid diagrams render in standard viewers; deprecated nodes have been removed, not greyed out.
- [ ] Cross-references resolve: every cited `FR-NNN` / `FT-NNN` / `FL-NNN` exists in the named owning doc.
- [ ] No claim of work-in-progress, blockers, or task status — those live in the operational queue.

If any box fails, the doc is in debt. Fix it in the same pass before continuing.

## See also

- Per-stack conventions skill — handback contract that names which docs were touched per delivery (e.g. `dotnet-conventions` § build-quality/handback-format for .NET).
- Per-stack test-designer skill — the role that fills the `## Test` FQN inside each `FL-NNN-*.md` (e.g. agent `dotnet-test-designer` for .NET).
- Stack-specific skills own concrete code catalogs (inspector codes, test-runner failure buckets, source-module names) — load the matching skill alongside this one when working on a typed stack.
