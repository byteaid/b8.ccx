# ccx — Claude Code Plugin Marketplace

**Version:** v1.0.0
**Updated:** 2026-05-24

Personal Claude Code marketplace. Each plugin lives in `plugins/<name>/` with its own `.claude-plugin/plugin.json`; the top-level `.claude-plugin/marketplace.json` is the catalog users add to their Claude Code install. Targets **Claude Code only** — no cross-provider deploy, no research substrate, no per-customer scoping.

## Authoring Rules

- All `.md` files MUST be written in **English**.
- Reader audience is **AI agents, not humans**: write maximally compact prose, omit filler, prefer lists/tables/code blocks over paragraphs, optimize for token cost without losing information.
- `.md` filenames: **no spaces**. Use `kebab-case` (lowercase, ASCII, hyphen-separated).
- **Authoring rules for agents and skills are owned by the Claude Code local meta-skills**, not by this file. Before creating or editing any artifact under `plugins/<plugin>/agents/` or `plugins/<plugin>/skills/`, load the matching local skill:
  - Agents → skill `claude-code-subagents`.
  - Skills → skill `claude-code-skills`.

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
- **Frontmatter schema, body composition, review checklist** — owned by the local skill `claude-code-subagents`. Load it before authoring or editing any agent file.
- Subagents themselves cannot spawn further subagents — only the main session agent can. Agents intended to dispatch other agents (e.g. `buildx`) MUST be runnable as the main session agent (`claude --agent <name>`).

### Skills

- Folder per skill: `plugins/<plugin>/skills/<skill-name>/`. Entry point `SKILL.md` (uppercase, per Claude Code convention).
- **Frontmatter schema, progressive-disclosure caps, invocation surfaces, sourcing & coherence discipline** — owned by the local skill `claude-code-skills`. Load it before authoring or editing any skill file.
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

Full spec — `<script-name>.md` template, authoring checklist, invocation patterns — is owned by the local skill `claude-code-skills` (section `scripts.md`). Load it before adding or editing any bundled script.

### Hooks

- Not currently used by any plugin in this marketplace. If added later, follow the Claude Code hook-event reference and embed in `plugin.json` (or the relevant agent / skill frontmatter) rather than touching the user's global `settings.json`.

## Standardized Document Header

Every authored `.md` (agents, skills, supporting docs) MUST carry the standardized header. Frontmatter for agent/skill artifacts is governed by their per-type meta-skill; this header sits **above** the YAML frontmatter only on freeform docs (e.g. this file), and is the first thing in the body for skill section files.

```markdown
# {Title}

**Version:** v{major}.{minor}.{rev}
**Updated:** YYYY-MM-DD
```

## Plugin: `buildx`

Orchestrator agent for end-to-end software delivery. Bundles:

- **Agents** (`plugins/buildx/agents/`):
  - `buildx` — orchestrator (designed as the main session agent).
  - `analyst` — owns `docs/REQUIREMENT.md`, `docs/GLOSSARY.md`, `docs/DATA-MODEL.md`, and `docs/features/**`.
  - `dotnet-architect` — owns `docs/SOLUTION.md` and the out-of-repo `todo.md`.
  - `dotnet-test-designer` — writes the per-flow real integration tests.
  - `dotnet-developer` — implements until tests pass.
  - `dotnet-reviewer` — second-pass review of the developer's output; owns the out-of-repo `debt.md`.
- **Skills** (`plugins/buildx/skills/`): `development-documentation`, `development-methodology`, plus the .NET / ASP.NET Core / Playwright / Aspire / EF Core / hexagonal-architecture / scripting / serialization / testing bundle (~37 skills).

Spec lives in `plugins/buildx/agents/buildx.md`. Subagent dispatch contract is described there — do not duplicate it here.

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
