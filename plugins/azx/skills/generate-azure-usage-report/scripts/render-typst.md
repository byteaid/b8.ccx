# render-typst

One-line summary: render `usage-report.json` (+ optional `narrative.json`) into a self-contained Typst document, optionally compiling to PDF.

## Purpose

Stage 10. Merges the deterministic report with the agent-authored narrative and writes `{stage-dir}/typst/`:

```
typst/
  main.typ      # self-contained: theme preamble + generated body
  report.typ    # generated body alone, for human inspection
  data.json     # merge of usage-report + narrative (traceability)
  logo.png      # only when a logo is themed/supplied
  fonts/        # only when {styles}/fonts/ ships brand fonts (compile uses --font-path fonts)
```

Layout: cover → Resumen Ejecutivo (narrative prose + ATENCIÓN callout + 4-KPI row + Hallazgos Clave + Matriz de Riesgos + Próximos Pasos) → the 6 numbered sections (data blocks + per-section Análisis callouts) → closing source block. When `narrative.json` is absent all data tables still render and the prose parts are omitted.

Status tokens in generic tables are colored automatically (rojo: Alta/Crítico/Vencido/Zombie · naranja: Media/Advertencia/Sobredimensionado/Saturado/Detection · verde: Baja/OK/Correcto/Prevention).

## Invocation

```bash
# Neutral (no style guide): brand-free grays, no company/logo
dotnet run ${CLAUDE_SKILL_DIR}/scripts/render-typst.cs -- --stage-dir ./run-2026-06 --compile

# Themed: .styles/ carries theme.json + logo
dotnet run ${CLAUDE_SKILL_DIR}/scripts/render-typst.cs -- \
  --stage-dir ./run-2026-06 --styles ./client/.styles --compile
```

## Arguments

| Name | Required | Description |
|---|---|---|
| `--stage-dir` | yes | Staging directory (reads `usage-report.json`). |
| `--narrative` | no | Narrative path. Default `{stage-dir}/narrative.json`. |
| `--styles` | no | Style-guide directory (`theme.json` + logo). Default `{stage-dir}/.styles` when present; **absent = neutral, brand-free output** (gray palette, no company/author/logo). |
| `--title` / `--company` / `--author` / `--logo` | no | Branding overrides; each wins over the style guide. |
| `--primary-color` / `--secondary-color` / `--panel-color` | no | Theme hex overrides. Neutral defaults `#6B7280` / `#374151` / `#F3F4F6`. |
| `--font` | no | Body font family name. Default `Segoe UI`. Font files ship in `{styles}/fonts/` (`.otf`/`.ttf`), copied to `typst/fonts` and passed to `typst compile` via `--font-path fonts`. |
| `--compile` | no | Run `typst compile [--font-path fonts] main.typ output.pdf` when the CLI is on PATH. |
| `--force` | no | Overwrite an existing `typst/`. |

Resolution order per value: CLI flag > `.styles/theme.json` > neutral default. `theme.json` known keys: `title`, `company`, `author`, `primaryColor`, `secondaryColor`, `panelColor`, `font`, `logo` (file inside `.styles/`; `logo.png` is picked up without a key; a `fonts/` subdirectory is picked up automatically). Unknown keys are reported to stderr and ignored. See `typst-output.md` § Theming.

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Success. |
| `1` | Write conflict, or `--compile` set but `typst` unavailable/failed (the `.typ` bundle IS written; stderr prints the manual command). |
| `2` | `usage-report.json` missing. |

## Side effects

- Reads: `{stage-dir}/usage-report.json`, optional `narrative.json`, optional logo.
- Writes: `{stage-dir}/typst/`, optional `{stage-dir}/output.pdf`.
- Network: none.

See `typst-output.md` for the full layout/theming reference.
