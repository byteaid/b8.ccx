# Folder layout — `docs/` and the operational temp folder

## Purpose

Every project the team manages keeps its documentation in two predictable locations so any agent — human or LLM — can find any document without grepping. The layout is identical across stacks.

## The two locations

### 1. `docs/` — desired-state, git-tracked

```
{repo-root}/
├── docs/
│   ├── REQUIREMENT.md                       # FR/NFR + feature index
│   ├── SOLUTION.md                          # infrastructure + apps + comms
│   └── features/
│       ├── FT-001-{kebab-feature-name}/
│       │   ├── feature.md                   # feature description, FR cross-links
│       │   └── flows/
│       │       ├── FL-001-{kebab-route-name}.md
│       │       └── FL-002-{kebab-route-name}.md
│       └── FT-002-{kebab-feature-name}/
│           ├── feature.md
│           └── flows/
│               └── FL-010-{kebab-route-name}.md
└── (no docs/archive — git log is the archive)
```

- **`docs/` lives at the repo root.** Never under `src/`, never under a subproject, never duplicated across modules. Multi-module repos still keep one `docs/` at the top.
- **`docs/` is git-tracked.** Never gitignored, never excluded from publish artefacts.
- **Filenames are case-sensitive and exact.** `REQUIREMENT.md` and `SOLUTION.md` are UPPERCASE singular. `feature.md` is lowercase. `FL-NNN-{kebab}.md` uses uppercase prefix + zero-padded number + kebab name (e.g. `FL-002-login-wrong-password.md`).
- **One canonical doc per concept.** No `REQUIREMENT_v2.md`, no `BUGS-OLD.md`. Supersession is in-place rewrite.
- **No `docs/archive/`.** The desired-state is the present state; the historical archive is `git log`.
- **No PROGRESS, no CHANGELOG, no ASSESSMENT, no CODE_INSPECTION, no ARCHITECTURE.** These were retired in v0.4.0 of this skill — their content has been folded into the new structure (ARCHITECTURE → SOLUTION) or deleted (PROGRESS, CHANGELOG, ASSESSMENT, CODE_INSPECTION → git log).

### 2. `${OS_TEMP}/aix-todo/{repo-basename}/` — operational queue, NOT git-tracked

```
${OS_TEMP}/aix-todo/{repo-basename}/
├── todo.md          # T-NNN tasks for the current iteration (rewritten per iteration)
├── backlog.md       # BL-NNN items currently open (closed items are deleted)
└── bugs.md          # BG-NNN bugs currently open (closed items are deleted)
```

- **`${OS_TEMP}`** resolves to `$env:TEMP` on Windows and `${TMPDIR:-/tmp}` on POSIX.
- **`{repo-basename}`** is the basename of the repo's working directory at bootstrap time (deterministic per project; multiple checkouts of the same repo share the temp folder unless renamed).
- **None of these files is git-tracked.** They churn continuously and have no audit value once their items close.
- **No archive policy, no compaction policy, no threshold checks.** Closed `BL-NNN` / `BG-NNN` rows are deleted from the file; the trace survives in `git log` via the commits that closed them.
- **Discoverability:** the orchestrator and every dispatched agent are told the live path explicitly in their brief — they do NOT search for it.

## Path resolution at runtime

```text
${OS_TEMP}/aix-todo/{repo-basename}/todo.md
${OS_TEMP}/aix-todo/{repo-basename}/backlog.md
${OS_TEMP}/aix-todo/{repo-basename}/bugs.md
```

On Windows (PowerShell): `$env:TEMP\aix-todo\{repo-basename}\todo.md`
On Linux/macOS (bash): `${TMPDIR:-/tmp}/aix-todo/{repo-basename}/todo.md`

The parent directory is created on first write. The orchestrator MUST ensure the folder exists before any agent tries to write inside it.

## Bootstrap shapes

| Variant | What gets created |
|---|---|
| **a** (empty repo) | Full `docs/REQUIREMENT.md` + at least one `docs/features/FT-NNN-*/feature.md` + at least one flow + `docs/SOLUTION.md` + the three operational files in temp. |
| **b** (docs-only) | Refine existing `docs/`; create any missing canonical files; seed temp files if absent. |
| **c1** (code-only, full reverse-engineer) | Infer `docs/REQUIREMENT.md`, `docs/features/**`, `docs/SOLUTION.md` from the code; seed temp files. |
| **c2** (code-only, minimal docs) | Only the three temp files. No `docs/` content seeded. |
| **c3** (code-only, no docs) | Nothing. Proceed directly to code change. |
| **legacy-docs** (old format detected) | Blocked. Offer migration to the new hierarchical structure; nothing else happens until the user accepts. |
| **existing-code-greenfield-docs** (code-only, full reverse + Aspire + tests) | Full reverse-engineer + Aspire enrolment + per-flow real tests written. |

See [bootstrap.md](bootstrap.md) for the full procedures.

## Enforcement

- A bootstrap script (one per stack) creates the empty canonical files; reviewers reject a project that ships a `docs/` shape inconsistent with this layout.
- Any agent that proposes adding a new file to `docs/` MUST justify why an existing canonical doc cannot carry the content. The bias is heavily toward extending an existing doc.
- Filename casing is enforced by repository conventions. Use exact strings; never let an editor lowercase or rename a canonical file.
- The temp folder is recreated on demand — never committed, never published.

## See also

- [skill.md](skill.md) — the master table of canonical docs and their owners.
- [id-taxonomy.md](id-taxonomy.md) — the ID system that ties the docs together.
- [bootstrap.md](bootstrap.md) — the entry-point procedure that creates the layout.
