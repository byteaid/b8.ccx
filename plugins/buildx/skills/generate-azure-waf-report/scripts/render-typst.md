# render-typst

One-line summary: turn `report-sets.json` into a self-contained Typst project under `{stage-dir}/typst/` and optionally compile it to `output.pdf`.

## Purpose

Stage 7. The pipeline's deliverable. Reads `report-sets.json`, accepts branding/meta options on the CLI, writes:

- `data.json` — the single declarative data file the templates `json()` from.
- `main.typ` — page setup + dispatch loop over pillars.
- `theme.typ`, `cover.typ`, `executive-summary.typ`, `pillar-section.typ`, `conclusion.typ` — bundled templates.

Optionally calls `typst compile` if the binary is on PATH and `--compile` is set; otherwise prints the manual command.

## When to use

- Final stage of the pipeline.
- Re-rendering after a branding change with no data changes.
- Iterating on Typst templates (the templates ship in the script as constants — edit them in-place, re-run with `--force`).

## When NOT to use

- The data layer changed. Re-run stage 6 first.

## Invocation

```bash
dotnet run ${CLAUDE_SKILL_DIR}/scripts/render-typst.cs -- \
  --stage-dir ./run-2026-04-28 \
  --title "Azure WAF Assessment — Contoso Production" \
  --company Contoso \
  --logo ./logo.png \
  --compile
```

## Arguments

| Name | Required | Description |
|---|---|---|
| `--stage-dir` | yes | Staging directory containing `report-sets.json`. |
| `--title` | no | Report title. |
| `--company` | no | Company name. |
| `--hide-company` | no | Hide company name in headers/footers. |
| `--logo` | no | PNG logo path; copied into `typst/logo.png`. |
| `--primary-color` | no | Hex color. Default `#1b6ec2`. |
| `--secondary-color` | no | Default `#6c757d`. |
| `--accent-color` | no | Default `#0dcaf0`. |
| `--compile` | no | Run `typst compile` if the CLI is on PATH. |
| `--force` | no | Overwrite an existing `typst/` directory. |

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Success (compilation may have failed; check stderr). |
| `1` | Write conflict. |
| `2` | `report-sets.json` missing. |

## Stdout / stderr contract

- stdout: silent.
- stderr: layout of generated files; `typst compile` results when `--compile` is set.

## Side effects

- Reads: `{stage-dir}/report-sets.json`, optional logo file.
- Writes: `{stage-dir}/typst/` (overwritten with `--force`), optional `{stage-dir}/output.pdf`.
- Network: none.

## Examples

```bash
dotnet run ${CLAUDE_SKILL_DIR}/scripts/render-typst.cs -- --stage-dir ./run
# → exit 0, writes ./run/typst/main.typ + data.json + theme.typ + ...
```

```bash
dotnet run ${CLAUDE_SKILL_DIR}/scripts/render-typst.cs -- --stage-dir ./run --compile
# → exit 0, also writes ./run/output.pdf when typst is on PATH
```
