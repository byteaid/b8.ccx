---
name: pull-desired-state
description: Slash-command procedure that synchronizes a connected remote work-item provider INTO the local desired state (remote → local). Reads the `docs/REMOTE-SYNC.md` ledger; refuses when no remote is connected; verifies the provider CLI is present; fetches the managed work items, diffs them three-way against the ledger base and the local docs, applies clean remote changes (and imports remote-only items) into `docs/features/**` and the operational queue, reconciles hierarchy, and refreshes the ledger. Surfaces every genuine conflict to the user instead of overwriting. Azure DevOps is the first provider (`bta-ado` via `ado:workitems`); provider-agnostic by design.
when_to_use: |
  - User-invocable only (`/pull-desired-state`): a deliberate remote → local synchronization the user triggers — never auto-invoked.
  - Invoke when: the team updated work items in the remote (Azure DevOps) and the local docs / operational queue must catch up; reconciling after others changed states, titles, or hierarchy remotely; importing remote-only items created outside this repo.
  - Requires a connected remote (`docs/REMOTE-SYNC.md` with `connected: true`). Fails clearly if absent or if the provider CLI is missing.
  - NOT for pushing local changes outward — that is `/push-desired-state`. NOT for first-time doc authoring — that is the bootstrap / `docs-reverse` flow.
user-invocable: true
disable-model-invocation: true
allowed-tools: Bash, PowerShell, Read, Glob, Grep, Edit, Write
---

# /pull-desired-state — synchronize remote → local

Deterministic procedure. The authoritative model (the ledger format, the Agile mapping, the
correlation tag, the hierarchy and conflict rules) lives in `development-documentation` §
remote-sync — read that leaf first; this skill only sequences the work and the gates. The concrete
provider command surface lives in the provider skill (`ado:workitems` for `bta-ado`); this skill
never duplicates it.

**Direction invariant: remote → local.** This command reads the remote and writes the local docs /
operational queue + the ledger. It never creates or mutates remote work items — that is
`/push-desired-state`.

## Phase 0 — Preconditions (fail fast)

1. **Connected?** Read `docs/REMOTE-SYNC.md`. If it is absent or `connected:` is not `true`, STOP.
   Reply verbatim:

   > This repo is not connected to a remote desired-state provider (`docs/REMOTE-SYNC.md` is absent
   > or `connected: false`). `/pull-desired-state` requires a connected remote. To connect, seed
   > `docs/REMOTE-SYNC.md` with a provider and `connected: true` (see `development-documentation` §
   > remote-sync § Connection lifecycle).

2. **Provider CLI present?** Resolve `provider:` from the ledger. For `ado`, verify `bta-ado
   --version` succeeds. If the CLI is absent, STOP and report which provider skill installs it
   (`ado:workitems` for `ado`) — do not improvise with another tool. For an unknown provider with
   no installed skill, STOP and say the provider is unsupported here.

3. **Read the ledger.** Parse the provider reference (org/project), the process template, and the
   full mapping table (internal id · remote type · remote id · correlation tag · last-synced state
   · last-synced hash). **Drift guard:** confirm the provider resolves to the same org/project the
   ledger records (`.bta/ado.json` for ADO); on a mismatch, STOP and ask — never sync against a
   re-pointed target.

## Phase 1 — Fetch (read-only on the remote)

1. Fetch the managed work items via the provider skill's machine-readable output, including each
   item's fields, tags, and relations (parent/children). WIQL matches **whole tags only** (no
   `bx:*` prefix), so enumerate by **OR-ing the exact `bx:<id>` tags from the ledger**, and — when
   the constant marker tag `bx` is in use — additionally sweep by that marker to catch items
   created outside this repo. To verify a specific item's parentage, fetch it by id (its relations
   carry `parent == null` for an orphan).
2. Build the remote snapshot: keyed by correlation tag → { remote id, type, state, title,
   description, parent, related }.
3. Note any items the user may want adopted: managed items not yet in the ledger (`remote-new`), or
   pre-existing untagged items (offer to back-fill a `bx:<id>` tag) — list them; do not import
   without the mapping step in Phase 3.

## Phase 2 — Diff (three-way)

For each ledger row, compare three versions per the conflict rule (`development-documentation` §
remote-sync § Conflict rule):

- **local now** — the current local doc / queue item.
- **ledger base** — last-synced state + hash.
- **remote now** — the Phase 1 snapshot.

Classify each item:

- `remote-only-change` — remote moved, local matches base → apply remotely-sourced change locally.
- `local-only-change` — local moved, remote matches base → leave (push will carry it).
- `unchanged` — neither moved → nothing.
- `conflict` — both moved → defer to Phase 4 (ask).
- `remote-new` — tagged item with no ledger row, or untagged item the user elected to adopt →
  import as a new local item with the mapped internal id.
- `remote-deleted` — ledger row whose remote item is gone → surface (do not auto-delete local).

Emit the **diff table**: one row per item — `internal id | remote type | classification | what
changed | resolution`. Present it and **STOP for user review** before any Phase 3/4 write.

## Phase 3 — Apply clean changes (local writes)

For every `remote-only-change` and `remote-new`, write the local side per the mapping:

- **Feature ← FT**, **User Story ← FL**: update / create the matching `docs/features/**` doc.
  Spec-text changes (title/description) route through the analyst's docs when running under
  `buildx`; otherwise edit the canonical file directly per the `development-documentation` leaves.
  Respect the desired-state invariant on every state-doc write (no history, IDs resolve).
- **Bug ← BG**: update / create the `BG-NNN` row in `${OS_TEMP}/aix-todo/{repo-basename}/bugs.md`.
- **Issue ← BL**: update / create the `BL-NNN` row in `backlog.md`.
- Assign fresh internal ids for `remote-new` items using the next free integer per
  `development-documentation` § id-taxonomy (never reuse a retired number).

## Phase 4 — Resolve conflicts (ask)

For each `conflict`, present both versions (local now vs remote now) and ask the user which wins,
per item. Never auto-pick. Apply the user's choice to the local side; the chosen value becomes the
new ledger base.

## Phase 5 — Reconcile hierarchy

Verify the pulled structure against `development-documentation` § remote-sync § Hierarchy
reconciliation: every Bug under its Feature, every User Story under its Feature, every Issue
`Related` to its Feature(s). On the pull side this is a **detection**: record any orphan or
mis-parent in the hand-off as a finding for `/push-desired-state` to correct remotely (pull does
not mutate the remote). Do not delete-and-recreate.

## Phase 6 — Refresh the ledger and hand off

1. Update `docs/REMOTE-SYNC.md`: add rows for imported items, update `last-synced state` and
   `last-synced hash` for every touched row, set `Last sync:` to now with `(pull)`.
2. Re-grep cited ids so cross-references still resolve; run the desired-state invariant checklist on
   every touched state doc.
3. Hand off: the diff table with final per-row status, files created / modified, conflicts asked
   and how the user resolved them, hierarchy findings for a follow-up push, and any
   `remote-deleted` / untagged-adoption decisions still open. Recommend committing (ledger + docs)
   with a message naming the sync; commit only if the user asks.

## Constraints

- **Remote → local only.** Never create or mutate remote work items here.
- **Fail closed.** Not connected or CLI absent → stop with the verbatim message; never auto-connect.
- **Never auto-resolve conflicts.** Both-sides-changed always asks the user.
- **Never auto-delete local** on a `remote-deleted` — surface it.
- **Idempotent.** Re-running with no remote change is a no-op beyond a refreshed `Last sync:`.
- **Respect the desired-state invariant** on every state-doc write; `REMOTE-SYNC.md` itself is the
  exempt integration ledger.
- **Provider details stay in the provider skill** — reference `ado:workitems` by name; do not
  inline `bta-ado` flags.

## Cross-references

- `development-documentation` § remote-sync — the ledger, mapping, hierarchy, and conflict canon.
- `push-desired-state` — the companion command (local → remote).
- `ado:workitems` — the ADO provider skill (`bta-ado` command surface).
- Sibling agents (when dispatching under `buildx`): `analyst` (spec-text writes), `dotnet-architect`.
