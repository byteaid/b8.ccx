# Data-flow document — `docs/features/FT-NNN-{kebab}/dataflows/DF-NNN-{kebab}.md`

## Purpose

A data-flow document answers: *exactly what must the code do to realise a route — where does the data enter, what happens to it at each stage, and on which specific infrastructure?* It is the **implementation contract**: the code and the data flow must be mappable step by step, in both directions. Where the flow file describes the route from the actor's perspective (WHAT the user observes), the data-flow file describes the same route from the data's perspective (HOW the system processes it).

It is read by: the developer (it is the source of truth for what to implement — every developer dispatch names the `DF-NNN`(s) to implement or correct), the reviewer (to verify the code↔data-flow mapping), the test-designer (to pick seed strategies and sharpen side-effect assertions), the architect (to keep the pipelines consistent with `SOLUTION.md`).

## Owner

The **architect** role produces and maintains every data-flow file — it is the only content the architect writes under `docs/features/`. The analyst never touches `dataflows/`; the developer and test-designer read only.

## Where

- Path: `docs/features/FT-NNN-{kebab}/dataflows/DF-NNN-{kebab}.md`, where `FT-NNN` is the owning feature and `{kebab}` describes the pipeline (`credential-validation`, `failed-attempt-counter`).
- Tracked: yes (git).
- Lifecycle: desired state — **rewritten in place** when the pipeline or its infrastructure changes. No history, no Decisions log.

## Cardinality — 1 flow = 1..N data flows

- Every `FL-NNN` that requires implementation work carries **at least one** data flow; a single route may decompose into several pipelines (e.g., the synchronous request/response pipeline plus the async event fan-out it triggers).
- A data flow belongs to **exactly one** parent `FL-NNN`. Two routes with similar pipelines still get separate `DF-NNN` files — same strictness as one-route-one-flow.
- The developer is never dispatched on a flow whose data flows are missing or stale — the architect's data-flow pass precedes implementation in the per-item cycle.

## What goes in

- Data-flow title (the pipeline, not the route).
- Parent flow (`FL-NNN`) and feature (`FT-NNN`).
- **Entry point:** the exact surface where the data enters (HTTP endpoint, queue subscription, UI form, CLI verb, timer) and the input shape.
- **Steps:** numbered; each names what happens to the data and the **specific infrastructure** it happens on. The infrastructure level is concrete: "Azure SQL table `Users` via EF Core", "Service Bus topic `orders`, subscription `billing`" — never "the database" or "a queue".
- **Terminal state:** where the data rests when the pipeline completes — rows written, messages emitted, response returned, files produced.
- **Error paths:** per failing step, what happens to the in-flight data (rolled back, dead-lettered, returned as 4xx/5xx, retried).

## What does NOT go in

- **Programming style.** No class names, no method signatures, no design patterns, no project layout, no idiom prescriptions — style is owned by the per-stack conventions skills (e.g. `dotnet-conventions`, `dotnet-hexagonal-architecture`).
- Actor-observable steps and UI behaviour — those live in the parent `FL-NNN-*.md`.
- Test FQNs — tests bind to flows, not to data flows; the `## Test` block lives in the flow file.
- Vendor selection rationale, costs, environment strategy — [solution.md](solution.md).

## Format

```markdown
# DF-NNN — {Pipeline title}

**Flow:** FL-NNN ({route title})
**Feature:** FT-NNN ({feature title})

## Entry point

- **Surface:** {exact entry: HTTP POST /api/login | Service Bus topic `orders`, subscription `billing` | UI form | CLI verb}
- **Input:** {data shape at entry}

## Steps

| # | What happens to the data | Infrastructure | Data out |
|---|--------------------------|----------------|----------|
| 1 | {transformation / validation / routing decision} | {specific component} | {shape leaving the step} |
| 2 | {...} | {...} | {...} |

## Terminal state

- {Rows written, messages emitted, response returned — where the data rests.}

## Error paths

- Step {N} fails → {what happens to the in-flight data}.
```

### Concrete example

```markdown
# DF-003 — Failed-attempt counter update

**Flow:** FL-002 (Login with wrong password)
**Feature:** FT-001 (Login)

## Entry point

- **Surface:** HTTP POST `/api/login` on Company.Product.Api
- **Input:** `{email: string, password: string}`

## Steps

| # | What happens to the data | Infrastructure | Data out |
|---|--------------------------|----------------|----------|
| 1 | Payload deserialised and validated (email format, both fields present) | Company.Product.Api request pipeline | validated credentials pair |
| 2 | User row loaded by email | Azure SQL table `Users` via EF Core | user row incl. `password_hash`, `failed_attempts` |
| 3 | Submitted password hashed and compared against `password_hash`; mismatch detected | Company.Product.Api | mismatch verdict |
| 4 | `failed_attempts` incremented by 1 and persisted | Azure SQL table `Users`, single UPDATE in the same transaction | updated counter |
| 5 | 401 response produced | HTTP response | `{"error":"invalid_credentials"}` |

## Terminal state

- `Users.failed_attempts` incremented by exactly 1 for the matched row.
- No session row created; no token issued.

## Error paths

- Step 2 finds no user → out of scope here: the unknown-email route is a different flow with its own data flow.
- Step 4 fails (SQL unavailable) → transaction rolled back, HTTP 500, nothing persisted.
```

## Rules

- **Step-by-step mappability is the contract.** Every step must be locatable in the code, in order; every meaningful data transformation in the code must trace back to a step. The reviewer enforces this with rule `code-maps-to-dataflow` at severity `blocker` — a deviation is either wrong code (developer corrects) or a stale data flow (architect rewrites); it never closes unresolved.
- **Infrastructure is named specifically and must exist in `SOLUTION.md`.** Every component a step cites resolves to a row in the Apps or Infrastructure tables. If a pipeline needs infrastructure not yet there, the architect updates `SOLUTION.md` in the same pass — never cites unlisted infrastructure.
- **The developer never silently deviates.** If a data flow is wrong, infeasible, or in conflict with the code reality, the developer stops and escalates; the architect rewrites the data flow first. Code that "improves on" the documented pipeline without the rewrite is a defect.
- **No style prescriptions.** A data flow that names classes, patterns, or method shapes has leaked into the conventions skills' territory — strip it.
- **Desired state only.** Rewritten in place; no history; motivation lives in the commit message.

## Lifecycle

- **Created** by the architect when implementation work on the parent flow is first sequenced (per-item data-flow pass) or at bootstrap (variants a / b / c1) — picks the next free global `DF-NNN`, places the file under the owning feature's `dataflows/` folder.
- **Updated in place** when the route, the pipeline decomposition, or the infrastructure changes.
- **Renamed:** `{kebab}` may change when the pipeline description changes; `DF-NNN` stays stable.
- **Deleted** when the parent flow is retired — same commit that deletes the `FL-NNN-*.md`.

## IDs

- `DF-NNN` — data flows; globally unique across the project (single global counter, like `FL-NNN`).
- `FL-NNN` — referenced as the parent flow; defined in the sibling `flows/` folder.
- `FT-NNN` — the owning feature; defined in the sibling `feature.md`.

## See also

- [flow.md](flow.md) — the parent route this pipeline realises (actor's perspective).
- [solution.md](solution.md) — the Apps / Infrastructure tables every step's component must resolve to.
- [id-taxonomy.md](id-taxonomy.md) — the `DF-NNN` numbering rules.
- Per-stack reviewer agent — enforces `code-maps-to-dataflow` (e.g. `dotnet-reviewer` for .NET).
