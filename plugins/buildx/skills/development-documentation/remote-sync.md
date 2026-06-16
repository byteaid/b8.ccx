# Remote desired-state sync — cross-cutting

## Purpose

Optionally externalize control of a project's desired state — and the change/bug tracking that
sits alongside it — to a remote work-item system, **while always keeping the local copy as the
working source of record.** Azure DevOps is the first concrete provider; the model is
provider-agnostic so a different backend can replace it later without reshaping the procedure.

Externalization is **opt-in and configurable**. By default a repo is NOT connected; nothing syncs.
A repo is connected exactly when `docs/REMOTE-SYNC.md` exists with `connected: true`. That file is
how any agent **remembers** the repo is connected — it is read at session start, never guessed.

## Mental model

- **Local stays canonical on disk.** The desired-state docs (`docs/**`) and the operational queue
  (`${OS_TEMP}/aix-todo/{repo-basename}/**`) remain the working copy. The remote holds a
  synchronized **projection** of them, not a replacement.
- **No 1:1 mapping.** The internal taxonomy does not map one-to-one onto any provider's work-item
  model. Map each internal concept to the **nearest native type without distorting data**; a
  concept with no faithful target stays **local-only** (never force-fit).
- **One ledger.** `docs/REMOTE-SYNC.md` records the provider, the connection flag, the provider
  reference (e.g. org/project), and the **internal-ID ↔ remote-id mapping**. It is git-tracked so
  the linkage is durable and shared across the team — but it is an **integration ledger, not a
  desired-state doc**, and is therefore **exempt from the desired-state invariant** (it legitimately
  carries live remote ids and last-synced state). See § The ledger.
- **Synchronization is user-triggered, never automatic.** The orchestrator does not push or pull on
  its own. Two user-invocable slash commands drive it: `pull-desired-state` (remote → local) and
  `push-desired-state` (local → remote). If no remote is connected, both **fail clearly** — they
  never silently create a connection.
- **Conflicts are surfaced, never auto-resolved.** When the same item diverged on both sides, the
  sync **stops and asks the user**. No silent overwrite in either direction.

## The ledger — `docs/REMOTE-SYNC.md`

Single file at the repo root under `docs/`. Git-tracked. Maintained by the orchestrator and by the
two sync commands — **not hand-edited** (the mapping table especially). Shape:

```markdown
# Remote sync ledger

> Integration ledger — git-tracked, but NOT a desired-state doc. Exempt from the desired-state
> invariant (it carries live remote ids and last-synced state on purpose). Maintained by the
> orchestrator and the `/pull-desired-state` / `/push-desired-state` commands; do not hand-edit
> the mapping table.

- **Provider:** ado
- **Connected:** true
- **Process template:** Agile
- **Organization:** myOrg
- **Project:** myProject
- **Last sync:** 2026-06-15T09:00:00Z (pull)

## Mapping

| Internal | Remote type | Remote id | Correlation tag | Last-synced state | Last-synced hash |
|----------|-------------|-----------|-----------------|-------------------|------------------|
| FT-001   | Feature     | 1234      | bx:FT-001       | Active            | a1b2c3           |
| FL-007   | User Story  | 1240      | bx:FL-007       | Active            | d4e5f6           |
| BG-014   | Bug         | 1251      | bx:BG-014       | New               | 778899           |
| BL-022   | Issue       | 1260      | bx:BL-022       | New               | aabbcc           |
```

- **Last-synced state** is the remote workflow state captured at the last sync (the basis for the
  three-way conflict check on the next sync).
- **Last-synced hash** is a short digest of the local item's content at the last sync. With it the
  sync distinguishes "local changed", "remote changed", and "both changed" (→ ask).
- The ledger is git-tracked, so it is the durable correlation source. When the optional marker tag
  (§ Mapping) is in use, the managed items can also be **re-enumerated from the remote** (WIQL
  matches whole tags — there is no `bx:*` prefix query, so a constant marker tag is what makes
  "list all managed items" a single query); each item's `bx:<ID>` tag then maps it back.

## Provider model

The `provider:` field discriminates the backend. The procedure (load ledger → diff → reconcile →
ask on conflict → apply → refresh ledger) is **provider-agnostic**. The concrete CLI/API surface is
owned by the **provider skill**, never duplicated here:

| Provider | `provider:` | Provider skill | Driver |
|----------|-------------|----------------|--------|
| Azure DevOps | `ado` | `ado:workitems` | `bta-ado` CLI |
| (future) | … | its own skill | … |

Adding a provider means adding its skill and a mapping section here; the slash commands and this
leaf keep their shape.

## Mapping — Azure DevOps (Agile process template)

Assumes the **Agile** process template (types: Epic, Feature, User Story, Task, Bug, Issue). For
Scrum/CMMI the type names differ (User Story → Product Backlog Item; Issue absent) — confirm the
template before syncing.

| Internal | ADO type | Hierarchy / link rule |
|----------|----------|------------------------|
| `FT-NNN` feature | **Feature** | child of an Epic when a theme grouping exists |
| `FL-NNN` flow | **User Story** | child of its feature's **Feature** |
| `BG-NNN` bug | **Bug** | child of the **Feature** owning its affected `FL-NNN` |
| `BL-NNN` backlog / change-request | **Issue** | a requested change to the local desired state; `Related`-linked to the Feature(s) it spawns once realized |
| (optional theme) | **Epic** | manual grouping only — never auto-created |
| `T-NNN` iteration task | — | **not synced** (too granular; the iteration queue churns locally) |
| `DT-NNN` technical debt | — | **not synced** (local-only) |

- **Correlation tag.** Every synced work item carries the tag `bx:<internal-id>` (e.g. `bx:FT-001`,
  `bx:BG-014`) — the idempotency key. Push **creates** an item only when no item carries its
  `bx:<id>` tag, otherwise it **updates** the tagged item.
- **Enumerating managed items.** WIQL tag matching is **whole-tag, not prefix** — there is no
  `bx:*` wildcard. So pull enumerates managed items by **OR-ing the exact `bx:<id>` tags held in
  the git-tracked ledger** (the ledger always has them). Optionally, every managed item also carries
  a **constant marker tag `bx`** (no colon); filtering by that single tag lists all managed items in
  one query — needed to discover items created outside this repo and to rebuild a lost ledger.
  Recommended; the ledger-OR path works without it.
- **`BL-NNN` → Issue** is how requested changes to the desired state are tracked alongside the live
  work. When a `BL` is realized into features/flows, its Issue is `Related`-linked to the resulting
  Feature(s); the Issue is not the parent of the Feature.

## Hierarchy reconciliation (ADO)

Work items must stay correctly hierarchized. On every sync, reconcile:

- Every **Bug** is parented under the **Feature** owning its affected flow (`BG-NNN` → its `FL-NNN`
  → that flow's `FT-NNN` → that feature's Feature work item). A Bug found without that parent — or
  under the wrong one — is **re-parented**.
- Every **User Story** is parented under its **Feature**.
- Every **Issue** (`BL-NNN`) is `Related`-linked to the Feature(s) it spawned (no parent link).

Re-parenting an existing item requires the provider's re-parent capability. For ADO this is the
provider skill's re-parent on update (idempotent — re-applying the same parent is a no-op). The
sync **re-parents in place**; it **never deletes-and-recreates** to fix hierarchy (that would lose
history/comments). If a future provider lacks in-place re-parent, the sync records the orphan in the
hand-off and asks the user rather than distorting data.

## Conflict rule — always ask

The basis for comparison is the ledger's last-synced snapshot (state + hash), giving a three-way
view: **local now**, **ledger base**, **remote now**.

- Only **local** changed since the base → push applies it / pull leaves it.
- Only **remote** changed → pull applies it / push leaves it.
- **Both** changed (a genuine conflict) → **STOP and ask the user**, per-item, with both versions
  shown. Never overwrite either side silently. The user's decision is then applied and the ledger
  base advanced.

## The two commands

- **`pull-desired-state`** — remote → local. Imports remote changes and new items, reconciles
  hierarchy, refreshes the ledger. Fails if not connected or the provider CLI is absent.
- **`push-desired-state`** — local → remote. Creates/updates items idempotently by correlation tag,
  parents bugs/stories under their features, links Issues to features, refreshes the ledger. Fails
  if not connected or the provider CLI is absent.

Both are user-invocable only and surface conflicts rather than resolving them. See the
`pull-desired-state` and `push-desired-state` skills.

## Connection lifecycle

- **Connect.** Seed `docs/REMOTE-SYNC.md` with `provider`, `connected: true`, the provider
  reference, and an empty mapping table.
- **Two files, two jobs — no overlap.** The provider's own config owns the *operational connection*
  (how the CLI authenticates and resolves the target at runtime); for ADO that is `.bta/ado.json`
  (org/project/tenant/account, resolved per `ado:workitems` § input resolution) — buildx never
  writes it. `docs/REMOTE-SYNC.md` owns the *buildx-side ledger* (provider, `connected` flag, the
  internal-ID ↔ remote-id mapping). The ledger **mirrors** org/project for readability and as a
  **drift guard** — it is not the runtime source of those values. If the ledger's org/project
  disagree with what the provider resolves (e.g. someone re-pointed `.bta/ado.json`), the sync
  **STOPs and asks** before touching either side, rather than syncing against the wrong project.
- **Remember.** Any agent reads `docs/REMOTE-SYNC.md` at session start; `connected: true` means the
  repo is externalized, `connected: false` or an absent file means it is not.
- **Disconnect.** Set `connected: false` (keep the file so the mapping is preserved if reconnected),
  or delete the file to forget the linkage entirely.

## Provider capabilities & constraints (ADO / `bta-ado`)

The ADO provider skill (`ado:workitems`) supplies the capabilities clean sync needs: a stable
`--json` contract on every subcommand (stdout = data, stderr = diagnostics, exit `2` = failure),
in-place re-parent on update, relations on fetch (`relations.parent == null` ⇒ orphan), tag merge,
tag/WIQL filters, process-template introspection, and an idempotent hierarchy upsert keyed by the
correlation tag. Two constraints shape the procedure:

- **Whole-tag WIQL matching** (no `bx:*` prefix) — enumerate via the ledger's exact `bx:<id>` tags
  (OR-ed) and/or the constant marker tag `bx`. See § Mapping § Enumerating managed items.
- **Tag-index lag** — the remote's WIQL tag index trails writes by a few seconds, so an idempotent
  upsert re-run immediately after a create can still duplicate. Persist the returned id mapping to
  the ledger right away and leave a short gap before re-pushing; never re-query the laggy index to
  decide create-vs-update when the ledger already holds the mapping.

The concrete flags/contract are owned by `ado:workitems`; this leaf does not duplicate them.

## Rules

- **Opt-in.** No repo is connected unless `docs/REMOTE-SYNC.md` says so. Bootstrap does not create
  it unless the user opts into externalization.
- **Local is the source of record on disk.** The remote is a projection; the canonical artifacts
  remain `docs/**` and the operational queue.
- **Map to the nearest type without distorting data.** Never force a concept into a type that
  misrepresents it; unmappable concepts (`T-NNN`, `DT-NNN`) stay local-only.
- **Idempotent by tag.** Create only when no item carries the `bx:<ID>` tag; otherwise update.
  Re-running a sync must not duplicate.
- **Never auto-resolve conflicts.** Both-sides-changed always asks the user.
- **Never delete-and-recreate to fix hierarchy.** Re-parent in place, or surface the orphan.
- **The ledger is maintained by the sync, not hand-edited.** The mapping table is machine-owned.
- **`REMOTE-SYNC.md` is exempt from the desired-state invariant** — it is an integration ledger,
  not a state doc.

## See also

- [skill.md](skill.md) — the canonical doc set; `REMOTE-SYNC.md` is the one git-tracked integration
  ledger exception.
- [id-taxonomy.md](id-taxonomy.md) § External mapping — the internal-ID ↔ remote-type correspondence.
- [folder-layout.md](folder-layout.md) — where `REMOTE-SYNC.md` sits in the `docs/` tree.
- `ado:workitems` — the ADO provider skill (the `bta-ado` command surface). Referenced by name; its
  CLI details are not duplicated here.
- Slash skills `pull-desired-state` and `push-desired-state` — the two synchronization commands.
