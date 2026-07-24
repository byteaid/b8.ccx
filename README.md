# ccx — Claude Code plugin marketplace (byteaid/b8.ccx)

**Version:** v1.1.0
**Updated:** 2026-07-23

A personal Claude Code marketplace. Each plugin is a self-contained directory under `plugins/` with its own `.claude-plugin/plugin.json`. The top-level `.claude-plugin/marketplace.json` is the catalog users add to their Claude Code install.

## Available plugins

| Plugin | Description |
|---|---|
| [`buildx`](./plugins/buildx) | Orchestrator agent for end-to-end software delivery: `buildx` + the `analyst` / `dotnet-architect` / `dotnet-developer` / `dotnet-test-designer` / `dotnet-reviewer` specialists, the `development-documentation` / `development-methodology` skills, five slash skills (docs/test upgrades, reverse-engineering, remote desired-state sync), and a .NET / ASP.NET Core / Playwright / Aspire / EF Core / Bootstrap knowledge bundle (~43 skills). |
| [`azx`](./plugins/azx) | Azure skills bundle: `azure-pricing-api` (dated cost quotes), `byteaid-assets-icons` (resolve/verify/download service icons), `azure-diagrams` (Azure composition layer over the `dgx` render targets), `generate-azure-solution-proposal` (9-section proposal, Typst → PDF), `generate-azure-usage-report` (monthly usage report, script pipeline + Typst → PDF). |
| [`dgx`](./plugins/dgx) | Provider-agnostic diagram formats: `typst-diagrams` (Typst/`fletcher` node-edge diagrams for PDF/print) and `vsdx-diagrams` (JSON spec → native Visio `.vsdx` via a bundled .NET 10 script — shapes, svg/raster icon nodes with auto-rasterization, glued connectors, group enclosures; no Visio required). |

## Installing

```text
# Add the marketplace (once)
/plugin marketplace add byteaid/b8.ccx

# Install plugins
/plugin install buildx@ccx
/plugin install azx@ccx
/plugin install dgx@ccx
```

For local development against this checkout:

```text
/plugin marketplace add D:\srcx\ByteAid\b8\b8.ccx
/plugin install buildx@ccx
```

After installation, restart Claude Code (or run `/reload-plugins`) so the new agents and skills are picked up.

## Using `buildx`

`buildx` is designed to run as the **main session agent** so it can spawn the specialist subagents (`analyst`, `dotnet-architect`, `dotnet-developer`, `dotnet-test-designer`, `dotnet-reviewer`). Subagents themselves cannot spawn other subagents.

```text
claude --agent buildx
```

The agent classifies the working directory (empty / docs-only / code-only / legacy-docs / steady-state), offers a bootstrap path when applicable, then drives a `reconcile → test → plan → implement → verify` cycle per `BL-NNN` / `BG-NNN` item, in one of three modes:

- `stepper` (default) — deliver one item, hand back.
- `scoped` — deliver only the IDs the user named.
- `auto` — drain backlog and bugs to closure.

See `plugins/buildx/agents/buildx.md` for the full specification.

## Updating

When a new commit lands on the default branch:

```text
/plugin marketplace update ccx
/plugin update buildx@ccx
/plugin update azx@ccx
/plugin update dgx@ccx
```

Non-interactive alternative (terminal): `claude plugin marketplace update ccx && claude plugin update <plugin>@ccx`.

`plugin.json` for each plugin omits an explicit `version`, so Claude Code uses the git commit SHA as the cache key — every commit counts as a new version and `/plugin update` always pulls the latest.

## Repository layout

```
.
├── .claude-plugin/
│   └── marketplace.json          # marketplace catalog
├── plugins/
│   ├── buildx/                   # 6 agents + ~43 skills
│   ├── azx/                      # 5 Azure skills
│   └── dgx/                      # 2 diagram-format skills
│       ├── .claude-plugin/
│       │   └── plugin.json       # plugin manifest (one per plugin)
│       └── skills/               # each skill is a folder with SKILL.md (+ optional scripts/)
├── AGENTS.md                     # authoring rules + per-plugin specs
└── README.md
```

Adding more plugins later: create a new folder under `plugins/`, drop a `.claude-plugin/plugin.json` inside, populate `agents/` and `skills/` as needed, and register it in `.claude-plugin/marketplace.json`.

## License

MIT. See individual plugins for any third-party content attribution.
