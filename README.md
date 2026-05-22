# b8-ccx — Claude Code plugin marketplace

A personal Claude Code marketplace. Each plugin is a self-contained directory under `plugins/` with its own `.claude-plugin/plugin.json`. The top-level `.claude-plugin/marketplace.json` is the catalog users add to their Claude Code install.

## Available plugins

| Plugin | Description |
|---|---|
| [`buildx`](./plugins/buildx) | Orchestrator agent for end-to-end software delivery: `buildx` + the `analyst` / `architect` / `dotnet-developer` / `dotnet-test-designer` specialists, the `development-documentation` skill, and a .NET / ASP.NET Core / Playwright knowledge bundle (~30 skills). |

## Installing

```text
# Add the marketplace (once)
/plugin marketplace add a3-diaz/b8-ccx

# Install the plugin
/plugin install buildx@b8-ccx
```

Replace `a3-diaz/b8-ccx` with the actual `owner/repo` once the repo is pushed to GitHub. For local development against this checkout:

```text
/plugin marketplace add D:\srcx\ByteAid\b8\b8.ccx
/plugin install buildx@b8-ccx
```

After installation, restart Claude Code (or run `/reload-plugins`) so the new agents and skills are picked up.

## Using `buildx`

`buildx` is designed to run as the **main session agent** so it can spawn the specialist subagents (`analyst`, `architect`, `dotnet-developer`, `dotnet-test-designer`). Subagents themselves cannot spawn other subagents.

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
/plugin marketplace update b8-ccx
/plugin update buildx@b8-ccx
```

`plugin.json` for each plugin omits an explicit `version`, so Claude Code uses the git commit SHA as the cache key — every commit counts as a new version and `/plugin update` always pulls the latest.

## Repository layout

```
.
├── .claude-plugin/
│   └── marketplace.json          # marketplace catalog
├── plugins/
│   └── buildx/
│       ├── .claude-plugin/
│       │   └── plugin.json       # plugin manifest
│       ├── agents/               # 5 agents
│       └── skills/               # ~37 skills (each is a folder with SKILL.md)
└── README.md
```

Adding more plugins later: create a new folder under `plugins/`, drop a `.claude-plugin/plugin.json` inside, populate `agents/` and `skills/` as needed, and register it in `.claude-plugin/marketplace.json`.

## License

MIT. See individual plugins for any third-party content attribution.
