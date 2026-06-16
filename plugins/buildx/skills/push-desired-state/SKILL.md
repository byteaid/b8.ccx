---
name: push-desired-state
description: Slash-command procedure that synchronizes the local desired state INTO a connected remote work-item provider (local → remote). Reads the `docs/REMOTE-SYNC.md` ledger; refuses when no remote is connected; verifies the provider CLI is present; for each in-scope local item (FT→Feature, FL→User Story, BG→Bug, BL→Issue) it creates the item when no remote item carries its `bx:<id>` correlation tag, otherwise updates the tagged item; parents bugs and stories under their feature, links Issues to the features they spawn, reconciles orphan hierarchy, and refreshes the ledger. Surfaces every genuine conflict to the user instead of overwriting. Azure DevOps is the first provider (`bta-ado` via `ado:workitems`); provider-agnostic by design.
when_to_use: |
  - User-invocable only (`/push-desired-state`): a deliberate local → remote synchronization the user triggers — never auto-invoked.
  - Invoke when: local desired-state docs / operational queue changed (new or edited features, flows, bugs, backlog items) and the remote (Azure DevOps) board must reflect them; publishing newly authored features/flows; fixing remote hierarchy (orphan bug not under its feature).
  - Requires a connected remote (`docs/REMOTE-SYNC.md` with `connected: true`). Fails clearly if absent or if the provider CLI is missing.
  - NOT for importing remote changes locally — that is `/pull-desired-state`. NOT for editing the local docs themselves — that is the analyst / architect flow.
user-invocable: true
disable-model-invocation: true
allowed-tools: Bash, PowerShell, Read, Glob, Grep, Edit, Write
---

# /push-desired-state — synchronize local → remote

Deterministic procedure. The authoritative model (the ledger format, the Agile mapping, the
correlation tag, the hierarchy and conflict rules) lives in `development-documentation` §
remote-sync — read that leaf first; this skill only sequences the work and the gates. The concrete
provider command surface lives in the provider skill (`ado:workitems` for `bta-ado`); this skill
never duplicates it.

**Direction invariant: local → remote.** This command reads the local docs / operational queue and
creates or updates remote work items + refreshes the ledger. The only local writes it makes are to
`docs/REMOTE-SYNC.md` (the ledger). It never rewrites a state doc — that is the analyst/architect's
job, surfaced via `/pull-desired-state` when the remote is authoritative for a change.

## Phase 0 — Preconditions (fail fast)

1. **Connected?** Read `docs/REMOTE-SYNC.md`. If it is absent or `connected:` is not `true`, STOP.
   Reply verbatim:

   > This repo is not connected to a remote desired-state provider (`docs/REMOTE-SYNC.md` is absent
   > or `connected: false`). `/push-desired-state` requires a connected remote. To connect, seed
   > `docs/REMOTE-SYNC.md` with a provider and `connected: true` (see `development-documentation` §
   > remote-sync § Connection lifecycle).

2. **Provider CLI present?** Resolve `provider:` from the ledger. For `ado`, verify `bta-ado
   --version` succeeds. If the CLI is absent, STOP and report which provider skill installs it
   (`ado:workitems` for `ado`). For an unknown provider with no installed skill, STOP and say the
   provider is unsupported here.

3. **Read the ledger** and the mapping table (internal id · remote type · remote id · correlation
   tag · last-synced state · last-synced hash). **Drift guard:** confirm the provider resolves to
   the same org/project the ledger records (`.bta/ado.json` for ADO); on a mismatch, STOP and ask —
   never sync against a re-pointed target.

## Phase 1 — Collect the local in-scope set

Gather the local items that map to remote types per `development-documentation` § remote-sync §
Mapping:

- **FT-NNN** (`docs/features/FT-*/feature.md`) → Feature.
- **FL-NNN** (`docs/features/FT-*/flows/FL-*.md`) → User Story (parent = its FT's Feature).
- **BG-NNN** (`bugs.md`) → Bug (parent = the Feature owning the bug's affected `FL-NNN`).
- **BL-NNN** (`backlog.md`) → Issue (`Related` to the Feature(s) it spawned, when known).
- **T-NNN / DT-NNN** → skipped (not synced).

For each, compute its current content hash and look up its ledger row (matched by internal id /
correlation tag).

## Phase 2 — Diff (three-way) and plan operations

Per item, compare **local now** vs **ledger base** vs **remote now** (fetch the tagged items'
current state via the provider skill's machine-readable output):

- `create` — no ledger row and no remote item carries `bx:<id>` → create.
- `update` — tagged remote item exists, local changed since base, remote matches base → update.
- `unchanged` — nothing to do.
- `conflict` — local changed AND remote changed since base → defer to Phase 4 (ask).
- `reparent` — remote item exists but its parent / link does not match the mapping → reconcile
  hierarchy (Phase 3 step).

Emit the **operation plan**: one row per item — `internal id | remote type | op | parent target |
notes`. Present it and **STOP for user review** before any remote mutation.

## Phase 3 — Apply (remote writes, idempotent by tag)

Order so parents exist before children: Epic (if any) → Features → User Stories / Issues → Bugs.
The provider's idempotent hierarchy upsert (keyed by the correlation tag) is the idiomatic bulk
path — author the tree with one key per node and let it converge; otherwise create/update per item.

1. **Create** missing items via the provider skill, stamping **both** the `bx:<id>` correlation tag
   and the constant marker tag `bx` on each, and setting the parent (Feature for a Story/Bug) at
   creation.
2. **Update** changed items via the provider skill (title/description/state per the mapping).
3. **Reconcile hierarchy** (`development-documentation` § remote-sync § Hierarchy reconciliation):
   parent each Bug under the Feature owning its affected `FL`, each User Story under its Feature,
   and `Related`-link each Issue to its Feature(s). Re-parent an existing mis-parented item **in
   place** (the ADO provider's re-parent on update is idempotent — same parent is a no-op). **Never
   delete-and-recreate** to fix hierarchy. If a future provider lacks in-place re-parent, record the
   orphan in the hand-off and ask the user.
4. Capture each item's resulting remote id and state, and **persist the id mapping to the ledger
   immediately** — the remote's tag index lags writes by a few seconds, so do not re-query it to
   decide create-vs-update, and leave a short gap before any re-push to avoid duplicating.

## Phase 4 — Resolve conflicts (ask)

For each `conflict`, present both versions (local now vs remote now) and ask the user which wins,
per item. Never auto-pick. Apply the user's choice (push the local value, or accept the remote
value into the next `/pull-desired-state`); the chosen value becomes the new ledger base.

## Phase 5 — Refresh the ledger and hand off

1. Update `docs/REMOTE-SYNC.md`: add rows for created items (internal id, remote type, remote id,
   tag), update `last-synced state` + `last-synced hash` for every touched row, set `Last sync:` to
   now with `(push)`.
2. Hand off: the operation plan with final per-row status (created / updated / re-parented /
   conflicted-resolved), remote ids assigned, any hierarchy gaps the provider CLI could not fix in
   place, and conflicts asked + how the user resolved them. Recommend committing the ledger; commit
   only if the user asks.

## Constraints

- **Local → remote only.** The only local write is the ledger; never rewrite a state doc here.
- **Fail closed.** Not connected or CLI absent → stop with the verbatim message; never auto-connect.
- **Idempotent by tag.** Create only when no remote item carries `bx:<id>`; otherwise update.
  Re-running must not duplicate items.
- **Never auto-resolve conflicts.** Both-sides-changed always asks the user.
- **Never delete-and-recreate to fix hierarchy.** Re-parent in place or surface the orphan.
- **Map to the nearest type without distorting data.** `T-NNN` / `DT-NNN` are not synced.
- **Provider details stay in the provider skill** — reference `ado:workitems` by name; do not
  inline `bta-ado` flags.

## Cross-references

- `development-documentation` § remote-sync — the ledger, mapping, hierarchy, and conflict canon.
- `pull-desired-state` — the companion command (remote → local).
- `ado:workitems` — the ADO provider skill (`bta-ado` command surface).
- Sibling agents (when dispatching under `buildx`): `analyst`, `dotnet-architect`.
