# Requirement document — `docs/REQUIREMENT.md`

## Purpose

REQUIREMENT.md answers: *what does this product do, for whom, and under what constraints?* It is the contract between the user (or product owner) and the engineering team. Every functional and non-functional requirement is numbered, traceable, and written as test-friendly assertions. It also acts as the **index of features**: it lists every `FT-NNN` defined in `docs/features/`.

It is read by: the analyst role (to keep `FR-NNN` and `NFR-NNN` aligned with user intent), the architect role (to choose components that meet each NFR in `SOLUTION.md`), the test-designer role (to know which acceptance criteria the per-flow tests must cover).

## Owner

The analyst role produces and maintains REQUIREMENT.md and every file under `docs/features/`. The owner uses focused interview questions to elicit goals and edge cases, and never invents requirements that the user did not state.

## Where

- Path: `docs/REQUIREMENT.md`.
- Tracked: yes (git).
- Lifecycle: desired state — **rewritten in place** when requirements change. No history sections, no Decisions log, no `(superseded by …)` annotations. The reason for any change lives in the commit message.

## What goes in

- One-line product summary.
- Goals: 3–5 bullets of user-visible outcomes the product must achieve.
- Actors: a table mapping each role to what it does.
- Functional requirements: one numbered `FR-NNN` per behaviour, with description, acceptance criteria as a checkbox list, business rules, out-of-scope notes.
- Non-functional requirements: one numbered `NFR-NNN` per quality attribute (latency, throughput, availability, security, accessibility, internationalization, observability).
- Feature index: a table listing every `FT-NNN` defined in `docs/features/`, with the FR-IDs it covers.
- Out-of-scope items at the document level (the explicit no-list).
- Open questions awaiting input.
- A glossary of terms in plain language.

## What does NOT go in

- Per-feature detail or per-flow detail — those live in `docs/features/FT-NNN-{kebab}/feature.md` and `.../flows/FL-NNN-{kebab}.md`.
- Technical implementation choices (frameworks, databases, endpoints, protocols) — those live in [solution.md](solution.md).
- Cost or vendor decisions — [solution.md](solution.md).
- Bug reports — those live in the operational queue (`${OS_TEMP}/aix-todo/{repo-basename}/bugs.md`). Even if a bug surfaced a missing requirement, the bug stays in `bugs.md` and the new requirement gets its own `FR-NNN` here.
- Decisions log, history, supersession trails — git log is the historical archive.

## Format

```markdown
# {Product / feature name} — Requirements

> One-line summary of what this delivers and why it matters.

## Goals

- {Goal 1 — user outcome}
- {Goal 2}

## Actors

| Actor | Description |
|---|---|
| {Role} | {What they do, what they need} |

## Functional requirements

### FR-001. {Requirement title}

{Clear description of what the system must do, written as user-visible behaviour.}

**Acceptance criteria:**
- [ ] {Specific, testable assertion}
- [ ] {Another assertion}

**Rules:**
- {Business rule that governs this requirement}

**Out of scope (for this FR):**
- {Explicit non-goal}

### FR-002. {Next requirement}

...

## Non-functional requirements

### NFR-001. {Quality attribute title}

{Constraint: latency target, availability percentage, accessibility level, security posture, etc.}

**Acceptance criteria:**
- [ ] {Measurable assertion (e.g., "P95 latency under 200 ms over 24 h")}

## Features

> One row per FT-NNN. Click into the feature folder for detail.

| ID   | Feature                | Folder                                    | Covers FRs        |
|------|-------------------------|--------------------------------------------|-------------------|
| FT-001 | Login                 | `docs/features/FT-001-login/`             | FR-001, FR-002    |
| FT-002 | Checkout              | `docs/features/FT-002-checkout/`          | FR-010, FR-011    |

## Out of scope

- {Document-level non-goal}

## Open questions

- [ ] {Unresolved question awaiting stakeholder input}

## Glossary

| Term | Definition |
|---|---|
| {Term} | {Plain-language definition — no jargon} |
```

## Lifecycle

- **Created** at project bootstrap as a title-only stub; populated when the first feature is refined.
- **Updated in place.** New behaviour gets a new `FR-NNN`. Changed behaviour rewrites the existing `FR-NNN` in place — the prior text is removed. The commit message names what changed and why.
- **No supersession trail.** `(superseded by FR-NNN)` annotations are forbidden. If the team genuinely needs to retain old text, that's what `git log` and `git blame` are for.
- **Closed when** the product is closed. The file is never deleted; if the product is retired, the repo itself is the archive.

## IDs

- `FR-NNN` — functional requirements; defined here, referenced everywhere.
- `NFR-NNN` — non-functional requirements; same.
- `FT-NNN` — features; listed in the Features table here, with details in `docs/features/FT-NNN-{kebab}/feature.md`.

See [id-taxonomy.md](id-taxonomy.md) for the cross-document rules.

## See also

- [feature.md](feature.md) — the per-feature doc this file indexes.
- [flow.md](flow.md) — the per-route doc each feature contains.
- [solution.md](solution.md) — the infrastructure / components / costs that realise the requirements.
