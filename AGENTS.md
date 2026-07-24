# ccx — Claude Code Plugin Marketplace

**Version:** v1.12.0
**Updated:** 2026-07-23

Personal Claude Code marketplace. Each plugin lives in `plugins/<name>/` with its own `.claude-plugin/plugin.json`; the top-level `.claude-plugin/marketplace.json` is the catalog users add to their Claude Code install. Targets **Claude Code only** — no cross-provider deploy, no research substrate, no per-customer scoping.

## Authoring Rules

- All `.md` files MUST be written in **English**.
- Reader audience is **AI agents, not humans**: write maximally compact prose, omit filler, prefer lists/tables/code blocks over paragraphs, optimize for token cost without losing information.
- `.md` filenames: **no spaces**. Use `kebab-case` (lowercase, ASCII, hyphen-separated).
- **Authoring rules for agents and skills live in this file** — see § Authoring Reference — Agents & Skills. Read it before creating or editing any artifact under `plugins/<plugin>/agents/` or `plugins/<plugin>/skills/`. (This marketplace targets Claude Code only and uses native Claude Code frontmatter directly — there is no abstraction or deploy-rewrite layer.)

## Repository Layout

```
.
├── .claude-plugin/
│   └── marketplace.json                        # marketplace catalog
├── plugins/
│   └── <plugin-name>/
│       ├── .claude-plugin/
│       │   └── plugin.json                     # plugin manifest
│       ├── agents/<agent-name>.md              # one file per agent
│       └── skills/<skill-name>/
│           ├── SKILL.md                        # skill entry point
│           ├── <section-or-subindex>.md        # level 2
│           ├── <subindex>/<leaf>.md            # level 3
│           └── scripts/                        # optional bundled executables
│               ├── <script-name>.<ext>         # the executable (.sh / .ps1 / .cs / .py / .js / .ts)
│               └── <script-name>.md            # paired doc (same basename)
├── AGENTS.md                                   # this file
├── CLAUDE.md                                   # `@AGENTS.md` redirect
└── README.md
```

- A plugin is a self-contained directory under `plugins/`. Adding a new plugin = new folder + `.claude-plugin/plugin.json` + `agents/` and/or `skills/` + entry in `marketplace.json`.
- `plugin.json` omits an explicit `version`: Claude Code uses the git commit SHA as the cache key, so every commit counts as a new version and `/plugin update` always pulls the latest.

## Artifact Types

### Agents

- One file per agent: `plugins/<plugin>/agents/<agent-name>.md`.
- **Frontmatter schema, body composition, and review checklist** — see § Authoring Reference — Agents & Skills → Agents.
- Subagents themselves cannot spawn further subagents — only the main session agent can. Agents intended to dispatch other agents (e.g. `buildx`) MUST be runnable as the main session agent (`claude --agent <name>`).

### Skills

- Folder per skill: `plugins/<plugin>/skills/<skill-name>/`. Entry point `SKILL.md` (uppercase, per Claude Code convention).
- **Frontmatter schema, progressive-disclosure caps, invocation surfaces, sourcing & coherence discipline** — see § Authoring Reference — Agents & Skills → Skills.
- Progressive disclosure: keep `SKILL.md` lean; push details into sibling section files (`<section>.md`) and sub-index trees (`<subindex>/<leaf>.md`). The agent only loads what it needs.

### Skill Scripts

When a skill needs to execute code (a deterministic check, a generator, a CLI wrapper), the executable lives next to the skill in a `scripts/` subdirectory and is copied verbatim to the deployed skill.

```
plugins/<plugin>/skills/<skill-name>/
  SKILL.md
  scripts/
    <script-name>.<ext>   # the executable
    <script-name>.md      # how and when to use it (paired by basename)
```

Hard rules:

- **Two-file duo, paired by basename.** `<script-name>.<ext>` + `<script-name>.md` share the same kebab-case, lowercase, ASCII basename. An executable without its paired `.md` is rejected — the `.md` is how the skill body and the model decide when to invoke it.
- **Flat layout.** `scripts/` has no sub-folders. If you need grouping, split into a separate skill.
- **Cross-platform = same basename, one shared `.md`.** Ship `<name>.sh` + `<name>.ps1` with byte-identical contracts (same args, same exit codes, same stdout shape).
- **Supported extensions:** `.sh` (bash), `.ps1` (PowerShell 7+), `.cs` (.NET 10 file-based app via `dotnet run`), `.py`, `.js` / `.ts` / `.mjs`. Avoid `.exe`.
- **Idempotent by default.** Re-running with the same input produces the same output and disk state. Mutations require an explicit `--apply` flag (or equivalent); dry-run is the default.
- **Non-interactive.** No TTY available — `read`, `Read-Host`, `input()` are forbidden. Pass everything via args.
- **Args, not env vars.** Env vars are reserved for ambient configuration (`CLAUDE_SKILL_DIR`, host-exposed secrets).
- **Exit codes mean something.** `0` = success, `2` = a specific gate-worthy failure (e.g. breaking change detected). Never return `0` on partial success.
- **Stdout is data, stderr is diagnostics.** Mixing them breaks pipelines.
- **One-shot only.** No daemons, watchers, or servers — those belong in their own host project.
- **No embedded secrets.** Read from env vars or a user-provided path.
- **Size discipline.** `<script-name>.md` stays under ~100 lines; the script itself under ~300 lines. Bigger = split or move into a host project.

Reference the script from the skill body via `${CLAUDE_SKILL_DIR}` so the path resolves regardless of cwd, and pre-approve the invocation in the skill frontmatter:

```yaml
allowed-tools:
  - Bash(${CLAUDE_SKILL_DIR}/scripts/<script-name>.sh *)
  - PowerShell(${CLAUDE_SKILL_DIR}/scripts/<script-name>.ps1 *)
```

The hard rules above are the full spec for bundled scripts — this section is self-contained.

### Hooks

- Not currently used by any plugin in this marketplace. If added later, follow the Claude Code hook-event reference and embed in `plugin.json` (or the relevant agent / skill frontmatter) rather than touching the user's global `settings.json`.

## Authoring Reference — Agents & Skills

Local, authoritative rules for writing the artifacts under `plugins/<plugin>/`. This marketplace uses **native Claude Code frontmatter directly** — no `type` / `version` / `updated` in agent or skill frontmatter (those belong only to freeform docs, see § Standardized Document Header), and no deploy-time field rewrite. Upstream truth: agents → <https://code.claude.com/docs/en/sub-agents>, skills → <https://code.claude.com/docs/en/skills>.

### Agents

A subagent is a bounded context the main thread spawns via the `Agent` tool. It receives **only** the file body as its system prompt (there is no default prompt), runs to completion, and returns one message. Pick a subagent over a skill when the work is bounded, has a structured return, and benefits from its own tool surface / permission posture.

**Frontmatter** (only `name` + `description` are required by Claude Code):

| Field | Notes |
|---|---|
| `name` | kebab-case, lowercase ASCII. MUST equal the filename basename. |
| `description` | **The trigger** — front-load the task shape so the dispatcher auto-routes; name what it is NOT for; add "use proactively" for aggressive delegation. Describe the trigger, never the implementation. |
| `model` | `opus | sonnet | haiku`; omit to inherit. Pin deliberately. |
| `effort` | reasoning effort; set only when the work needs more/less than the session default. |
| `maxTurns` | hard turn cap for bounded / diagnostic agents; omit for open-ended work. |
| `tools` | comma-separated **allowlist** — the minimum the work needs (read-only for research agents). Omit = all tools. An agent that dispatches others must include `Agent`. |
| `skills` | comma-separated skills preloaded into the agent's context. Subagents do **not** inherit the parent thread's skills — preload what is always needed; preload pays the full body cost on every spawn. |

**Body** (= the whole system prompt). Terse, second person, present tense, English only, no emojis. Order:

1. **Role** — one sentence.
2. **Responsibilities** — what it owns and the explicit boundaries (what it never touches; name the owning agent).
3. **Method** — numbered, deterministic; cite tools and skills by name.
4. **Hand-offs** — the exact return shape the caller parses (a fixed Markdown/JSON block, or "one paragraph, no preamble").
5. **Constraints** — hard MUST / MUST-NOT that a delegation prompt cannot override.

**Rules.** Subagents cannot spawn subagents — only the main session agent can; an agent that dispatches others (e.g. `buildx`) must be runnable as `claude --agent <name>`. Don't restate the description in the body. Trim the tool surface aggressively; broaden only when the agent fails on real work.

### Skills

A skill is **dormant content** the host attaches when its description matches the task; it injects a prompt fragment plus pre-approved tool grants and shares the calling agent's context (unless `context: fork`). It does not execute on its own.

**Frontmatter** (only `description` is recommended; `name` defaults to the folder):

| Field | Notes |
|---|---|
| `name` | kebab-case, lowercase ASCII, ≤ 64 chars; matches the folder name. |
| `description` + `when_to_use` | Front-load trigger keywords + task shapes. Use the snake_case `when_to_use` — Claude Code does not recognise the hyphenated form. Combined ≤ **1,536 chars** (runtime listing cap). |
| `allowed-tools` | **Pre-approval, not restriction** — listed tools run without a permission prompt while the skill is active; unlisted tools still work but prompt. |
| `user-invocable` | **Default `false`** (model-invocable only). Set `true` only for a user-typed slash command, and justify it in `when_to_use`. |
| `disable-model-invocation` | `true` blocks auto-invoke (and preload). `disable-model-invocation: true` + `user-invocable: true` = a pure slash command. |
| `context` / `agent` | `context: fork` runs the body in a subagent (pin `agent:` for determinism); the caller sees only the return. |

**Progressive disclosure** (the core discipline):

- `SKILL.md` (L1) is the only file auto-loaded — keep it a lean index: rules + dispatch table. Everything below is `Read`-on-demand.
- Up to **3 levels**: `SKILL.md` → `<section>.md` (L2, content or sub-index) → `<subindex>/<leaf>.md` (L3).
- **≤ 500 lines per file** (authoring cap; split deeper rather than relax it).
- After compaction only the **first ~5,000 tokens** of `SKILL.md` survive — put the rules + dispatch table at the top. Long tables and code samples > 30 lines go in L2/L3, linked from the dispatch table.

**Cross-references.** Cite another skill by **name only** — `` `other-skill` `` or `` `other-skill` § topic `` — never a Markdown link to another skill's `.md` (cross-skill paths rot). **Intra-skill** sub-file links inside the same folder ARE mandatory — they drive progressive disclosure. English only, no emojis. Filenames kebab-case, lowercase, ASCII, no spaces.

### Quick pick

| Need | Artifact |
|---|---|
| Inject a doc / recipe when the task matches | **skill** |
| User-typed verb, no auto-trigger (e.g. `/commit`) | **slash command** = skill with `disable-model-invocation: true` + `user-invocable: true` |
| Separate context with its own tools / turns / permission mode | **subagent** |
| Run on a tool/event without the model deciding | **hook** (see § Hooks) |

## Standardized Document Header

Every authored `.md` (agents, skills, supporting docs) MUST carry the standardized header. Frontmatter for agent/skill artifacts is governed by § Authoring Reference — Agents & Skills; this header sits **above** the YAML frontmatter only on freeform docs (e.g. this file), and is the first thing in the body for skill section files.

```markdown
# {Title}

**Version:** v{major}.{minor}.{rev}
**Updated:** YYYY-MM-DD
```

## Plugin: `buildx`

Orchestrator agent for end-to-end software delivery. Bundles:

- **Agents** (`plugins/buildx/agents/`):
  - `buildx` — orchestrator (designed as the main session agent).
  - `analyst` — owns `docs/REQUIREMENT.md`, `docs/GLOSSARY.md`, `docs/DATA-MODEL.md`, and `docs/features/**` minus the `dataflows/` subfolders.
  - `dotnet-architect` — owns `docs/SOLUTION.md`, the per-flow data flows (`docs/features/FT-*/dataflows/DF-NNN-*.md` — the step-by-step implementation contract, 1..N per `FL-NNN`), and the out-of-repo `todo.md`.
  - `dotnet-test-designer` — writes the per-flow real integration tests.
  - `dotnet-developer` — implements until tests pass.
  - `dotnet-reviewer` — second-pass review of the developer's output; owns the out-of-repo `debt.md`.
- **Skills** (`plugins/buildx/skills/`): `development-documentation` (now including the **remote-sync** canon — optional externalization of the desired state to a remote work-item provider via the `docs/REMOTE-SYNC.md` integration ledger), `development-methodology`, five user-invocable slash skills — `/dotnet-test-upgrade` (conforms an existing test suite to the current `dotnet-testing` canon via the documentation > tests pipeline, purging non-conforming code and dispersed artefacts), `/docs-upgrade` (conforms existing legacy- or current-format docs to the `development-documentation` canon and complements gaps, asking the user when a decision is not derivable; refuses on zero docs and points to `/docs-reverse`), `/docs-reverse` (reverse-engineers a codebase into the canonical doc set, asking the user on every ambiguity; optional path argument switches to migration mode — source codebase or output folder — e.g. the first step of an Angular→Blazor rebuild), `/pull-desired-state` and `/push-desired-state` (synchronize the desired state against a connected remote work-item provider — remote → local and local → remote; idempotent by `bx:<id>` correlation tag, reconcile work-item hierarchy, always ask on conflict; fail clearly when no remote is connected) — plus the .NET / ASP.NET Core / Playwright / Aspire / EF Core / hexagonal-architecture / scripting / serialization / testing / Bootstrap 5.3 (`bootstrap-css`) bundle (~43 skills total).
- **Optional provider dependency:** the ADO sync commands drive Azure DevOps through `ado@byteaid-tools` (`ado:workitems` / the `bta-ado` CLI); when that plugin/CLI is absent the sync commands degrade with a clear message rather than improvising. The model is provider-agnostic — a future backend adds its own provider skill without reshaping the commands.

Spec lives in `plugins/buildx/agents/buildx.md`. Subagent dispatch contract is described there — do not duplicate it here.

## Plugin: `dgx`

Diagram authoring/export bundle — **skills only, no agents**, provider-agnostic (no Azure knowledge; domain layers such as `azure-diagrams` sit on top). One skill per render format:

- **Skills** (`plugins/dgx/skills/`):
  - `typst-diagrams` — generic Typst/`fletcher` node-edge diagrams for PDF/print deliverables: import/version pinning, helpers, diagram/node/edge/`enclose` recipes, local image embedding (Typst has no network access), traps, compile checklist. Icon/image obtention is the caller's job.
  - `vsdx-diagrams` — native, editable Visio `.vsdx` from a declarative JSON spec (nodes / edges / groups) via the bundled `scripts/json-to-vsdx.cs` .NET 10 file-based script — pure BCL (no NuGet packages, no Visio required to generate), deterministic byte-stable output, glued 1-D connectors, dashed group enclosures, auto or explicit layout, image nodes (svg/png/jpg/gif as Foreign objects, deduplicated media parts; SVGs auto-rasterized through the typst CLI since the VSDX format only carries raster). Verified against real Visio (COM open + PNG export).

## Plugin: `azx`

Azure skills bundle — **skills only, no agents**: three reusable base skills (pricing, icons, diagrams) plus two deliverable skills (proposal generation, usage report). The base skills are layered: `azure-diagrams` consumes `byteaid-assets-icons` for icon obtention and `dgx`'s `typst-diagrams` / `vsdx-diagrams` for the generic render targets; the proposal consumes all three azx base skills.

- **Skills** (`plugins/azx/skills/`):
  - `azure-pricing-api` — Azure Retail Prices API (`https://prices.azure.com/api/retail/prices`) reference: filters, pagination, Consumption / Reservation / savings-plan rates, and the quote workflow with its pitfall catalog.
  - `byteaid-assets-icons` — ByteAid Assets icons API (`https://assets.byteaid.io/api/icons/*`): icon-slug **resolution** (fuzzy search, never-empty pitfall), URL **verification**, and **download** to a local SVG. Pure icon-obtention base — embedding into a diagram is `azure-diagrams`'s job.
  - `azure-diagrams` — Azure composition layer for architecture/topology diagrams: render-target selection by consumption surface (mermaid img-in-label; Typst via `dgx`'s `typst-diagrams`; Visio via `dgx`'s `vsdx-diagrams`, downloaded SVGs referenced directly; GitHub caveats incl. stripping external imgs) plus the Azure icon discipline (icon-is-the-node, one resolved slug per service, standard sizes). Keeps the mermaid recipe and the Azure deltas over the generic Typst recipe. Consumes `byteaid-assets-icons`; reused by the proposal and available standalone.
  - `generate-azure-solution-proposal` — deterministic procedure for a 9-section Azure solution proposal (Resumen ejecutivo → Fuera de alcance), Typst → PDF; consumes the three base skills above (pricing for cost, icons for resolution/download, diagrams for the Arquitectura diagram); optional style guide themes appearance via a fixed `theme` dict without altering anatomy.
  - `generate-azure-usage-report` — compact monthly "reporte light" of how a subscription used Azure over the last 3 months (last month as protagonist): executive summary for leadership, consolidated costs (spend trend, zombie/oversized + unused resources), security posture (Defender + the WAF sub-report: attacks, false-positive candidates), reliability (backup coverage per family — VMs, SQL PITR/LTR, Cosmos, storage soft delete, App Service Backup), availability (TLS certificates incl. App Gateway listeners resolved against Key Vault, availability zones, regional redundancy), performance (saturation + scaling rules), and operational hygiene (naming, tagging, fragmentation) — every finding environment-weighted (Prod vs Dev/Test) and each section with Advisor-grounded recommendations. 10-stage pipeline of .NET 10 file-based C# scripts (`scripts/` with paired `.md` contracts) + one agent-authored Spanish narrative (frozen prompt) + Typst → PDF render. NOT the deep Well-Architected Framework assessment (that pipeline lives outside this marketplace). Note: the acquisition/aggregation/render scripts intentionally exceed the ~300-line script cap — they are a fixed pipeline toolchain, documented per script, and re-run rather than edited during report generation.

## Distribution & Updates

Install:

```text
/plugin marketplace add byteaid/b8.ccx
/plugin install buildx@ccx
```

Local development against this checkout:

```text
/plugin marketplace add D:\srcx\ByteAid\b8\b8.ccx
/plugin install buildx@ccx
```

Update after a new commit lands on the default branch:

```text
/plugin marketplace update ccx
/plugin update buildx@ccx
```

Restart Claude Code (or `/reload-plugins`) so new agents and skills are picked up.
