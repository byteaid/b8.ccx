# Bugs document — `${OS_TEMP}/aix-todo/{repo-basename}/bugs.md`

## Purpose

bugs.md answers: *what defects are open against this product, with what severity and reproduction?* It is the defect queue, parallel to but separate from the feature backlog. Every defect carries a `BG-NNN`, a severity, and a reproduction recipe.

## Owner

The **orchestrator** role produces and maintains bugs.md. New bugs typically arrive from three sources: the user reports one, a test fails surfacing one, or a code review catches one. In all cases the orchestrator records the bug here.

## Where

- Path: `${OS_TEMP}/aix-todo/{repo-basename}/bugs.md` (operational queue, outside the repo).
- Tracked: **no** (not git-tracked).
- Lifecycle: open items only. **Closed items are deleted** — the trace lives in `git log` (the commit that landed the fix + the test that prevents regression).

## What goes in

- A table of open `BG-NNN` bugs with severity, observed behaviour, expected behaviour.
- For severity-High and above: an inline detail block with full reproduction steps and a back-reference to the affected `FL-NNN` (so the test-designer knows which flow's test must catch the regression).
- "Wontfix / by-design" decisions only when the user has explicitly accepted the bug as not-a-bug. Keep these as a small footer table; they are reference, not work.

## What does NOT go in

- Closed bugs. They are deleted on close. The git history of the affected test file is the record.
- Features — those live in [backlog.md](backlog.md).
- Per-task decomposition — that lives in [todo.md](todo.md).
- Test infrastructure flakes that are not product defects — those are handled inline as the test-designer adjusts the test.

## Format

```markdown
# Bugs

> Bugs currently open. Closed bugs are deleted; the fix lives in git log + the regression test.

## Open

| ID     | Title                                           | Severity | Affected flow | Observed                                            | Expected                                  |
|--------|-------------------------------------------------|----------|---------------|-----------------------------------------------------|-------------------------------------------|
| BG-014 | Cancel order does not recompute total           | High     | FL-007        | Total still includes cancelled line item            | Total recomputes from remaining items     |
| BG-015 | Catalog dropdown misses entries created same session | Medium | FL-031     | New types missing from form dropdown                | New types appear without a refresh        |

### BG-014. Cancel order does not recompute total

- **Severity:** High
- **Affected flow:** FL-007 (Cancel order — happy path)
- **Observed:** Calling cancel on a single line item leaves the order's stored total unchanged.
- **Expected:** The total recomputes from the remaining (non-cancelled) line items.
- **Reproduction:**
  1. Place an order with two items at 50 each.
  2. Cancel the second line item.
  3. Read the order — total still shows 100.
- **Likely fix area:** the cancellation handler in the Orders API. The current test for FL-007 does not assert the total — the test-designer must extend it before the developer fixes.

## Wontfix / by-design

| ID     | Title                                              | Decision                                                      |
|--------|----------------------------------------------------|---------------------------------------------------------------|
| BG-006 | Two-character search returns too many results      | By-design — minimum 3 characters per UX agreement (FR-022).  |
```

## Lifecycle

- **Created** at project bootstrap as a stub with an empty Open table.
- **Updated** when:
  - a new bug is reported → new `BG-NNN` row appended; for severity-High and above, an inline detail block;
  - the orchestrator picks a bug → the row stays;
  - the bug is fixed → the row is **deleted**.
- **Severity scale is mandatory:** Critical / High / Medium / Low.
- **Never close a bug without a regression test in place.** The bug-fix cycle is: analyst confirms the desired state is correct → test-designer ensures the affected flow's `## Test` asserts the bugged invariant → developer makes the test pass. Only after the test exists and passes is the `BG-NNN` row deleted.
- **Reproduction recipes are mandatory** for any bug above `Low` severity. A bug without a reproduction is not actionable; either get the recipe or downgrade to `observation` and move it elsewhere.

## Rules

- **One canonical bugs.md per project** at the temp path. Do NOT create per-area bug lists.
- **Affected flow is mandatory.** Every bug references the `FL-NNN` it breaks. If no flow covers the broken behaviour, the analyst must first add the missing flow (and the test-designer must add its test); the bug becomes the request that drives flow creation.
- **No "Closed" or "Past iterations" sections.** When closing a bug, delete the row.
- **`Wontfix / by-design` footer is allowed** as a small reference table — items that came up, were debated, and explicitly accepted as not-a-bug. These are not work-in-flight; they live here for posterity within the lifespan of the temp folder.

## IDs

- `BG-NNN` — bugs; counter is local to this file. Numbering does NOT reset on close.
- `FL-NNN` — referenced in "Affected flow"; defined in `docs/features/FT-*/flows/FL-*.md`.

## See also

- [backlog.md](backlog.md) — feature queue (separate axis).
- [todo.md](todo.md) — the per-iteration decomposition of the picked bug-fix.
- [flow.md](flow.md) — the route the bug affects; its `## Test` is the regression-prevention contract.
