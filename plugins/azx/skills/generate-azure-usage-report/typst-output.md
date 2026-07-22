# Typst Output — Light Usage Report

**Version:** v1.2.0
**Updated:** 2026-07-21

L2 leaf. `render-typst` (stage 10) reads `usage-report.json` + optional `narrative.json`, merges them into `data.json`, and writes a Typst project under `{stage-dir}/typst/`. The document body and all tables/callouts are generated in C# as **literal Typst markup**; the theme + reusable helpers live in an embedded preamble. Output is **Spanish** (identifiers/comments in the script stay English).

## Project assembly

```
{stage-dir}/typst/
  main.typ      # self-contained: theme preamble + generated body
  report.typ    # generated body alone, for human inspection (not imported)
  data.json     # merge of usage-report + narrative (traceability)
  logo.png      # only when a logo is themed/supplied
  fonts/        # only when {styles}/fonts/ ships brand fonts
```

`main.typ` is single-file on purpose (Typst `#include` does not share the includer's `#let` scope). The templates are embedded string literals in `static class Templates` / `static class TypstWriter` inside `render-typst.cs` — layout changes are made there, then re-run stage 10.

## Theming — neutral by default, `.styles/` supplies branding

**Whitelabel invariant (azx convention):** with no style guide the report is brand-free — neutral gray palette, no company, no author, no logo. Branding enters EXCLUSIVELY through a style-guide directory (`--styles DIR`, auto-discovered at `{stage-dir}/.styles` when the flag is omitted):

```
.styles/
  theme.json   # { "title"?, "company"?, "author"?, "primaryColor"?, "secondaryColor"?, "panelColor"?, "font"?, "logo"? }
  logo.png     # picked up automatically when theme.json has no "logo" key
  fonts/       # optional brand font files (.otf/.ttf) — copied to typst/fonts, compile adds --font-path fonts
```

Merge rule: CLI flag > `theme.json` value > neutral default. Known key → override; unknown key → stderr note and ignored; `logo` names a file inside `.styles/`. A style guide changes appearance ONLY — never structure.

| Value | Neutral default | Themed by | Notes |
|---|---|---|---|
| title | `Reporte de Uso de Azure` | flag / theme.json | Cover + running header. |
| company | (none) | flag / theme.json | Cliente on cover / closing. |
| author | (none) | flag / theme.json | `Elaborado por` on cover + closing credit. |
| logo | (none) | flag / `.styles/logo.png` | Copied to `typst/logo.png`, cover renders it at 180pt. |
| primaryColor | `#6B7280` (gray) | flag / theme.json | Heading rules, accents. |
| secondaryColor | `#374151` (dark gray) | flag / theme.json | Body text, table headers. |
| panelColor | `#F3F4F6` | flag / theme.json | KPI blocks, info boxes, zebra odd rows. |
| font | `Segoe UI` | flag / theme.json | Body font family. Ship the files in `.styles/fonts/` unless system-installed. |

Non-branding flags: `--narrative` (default `{stage-dir}/narrative.json`, empty-graceful), `--compile`, `--force`. The semáforo palette (rojo `#C62828`, naranja `#E65100`, verde `#33691E`, azul `#1565C0`) is semantic, baked into the preamble, and never themed.

## Page + type setup

A4 portrait, margins 2.5/2/2/2 cm; body = the themed font (default `"Segoe UI"`) 10pt, `hyphenate: false`; inline raw `"Consolas"` 9pt boxed. Running header from page 2; page-count footer on page 1. L1 headings 16pt with a 2pt primary-color underline; zebra tables (header row = secondary color with white bold text, `calc.odd(y)` → panel).

## Helpers (preamble)

- `callout(color, icon, title, body, breakable: true)` — used for ATENCIÓN (naranja ⚠), Resumen (azul ℹ), Observaciones (naranja ⚠), Recomendaciones (verde ✔).
- `kpi(label, value, sub)` — executive-summary KPI blocks.
- Inline raw (`` `...` ``) styles resource names / ARM ids as code boxes.

## Document layout

1. **Cover** — logo, title, `Período · {range}`, info-box grid (Cliente / Suscripción / Período / Generado / Elaborado por), confidential footer.
2. **Resumen Ejecutivo** (from `narrative.executiveSummary`): `estadoGeneral` prose → ATENCIÓN callout → **4-KPI row** from `signals` (Costo del mes, Variación mensual, Secure Score, Recursos) → Hallazgos Clave (bold-lead bullets) → **Matriz de Riesgos** (probabilidad cell colored Alta=rojo / Media=naranja / Baja=verde) → Próximos Pasos (Inmediato / Corto plazo / Mediano plazo).
3. **Numbered sections 1-6** — flowing continuously (no per-section pagebreak; compactness is the product): L1 heading, the section's FIXED canonical blocks as L2 sub-sections rendered by `displayType` (each block opens with its `description` as a small italic intro line stating what it evaluates; an empty block renders "Sin hallazgos en el período."), then ONE Análisis group (Resumen / Observaciones Clave / Recomendaciones callouts) from `narrative.sections.{id}`.
4. **Closing** source block — data sources, subscription, period, generation credit.

There is NO conclusion chapter — the light report front-loads everything decision-relevant into the executive summary.

## Rendering by `displayType`

| displayType | Rendered as |
|---|---|
| `MonthlyTrend` | **Column chart** on a light panel: per month a bold value label, a proportional column (current month = full primary color, prior months lightened), the month name, and the MoM delta with colored arrow (⬆ rojo up, ⬇ verde down, – flat). Pure Typst (`box` rects) — no chart packages, fully deterministic. |
| `BarList` | **Horizontal bar list** (`grid`: label 5.2cm · proportional bar · bold value). Bars scale to the row max; `color` maps to the semáforo palette (rojo/naranja/verde/azul), absent = primary. Used for sizing classification, WAF actions, and security severity. |
| `GroupedMonthly` | Zebra table Concepto × month columns, bold Total row. |
| `CountTable` | Zebra table Concepto / Cantidad, bold Total row. |
| `Table` | Generic zebra table from `headers`/`rows`; per-column `align` (`l`/`r`/`c`); drops to 8pt when `dense`, >15 rows, or >5 columns. Text (`l`) columns get fractional widths (first 1.6fr, rest 1fr) and numeric/centered columns auto-size; long unbroken tokens (ARM names, URIs, thumbprints) receive zero-width-space break opportunities so they wrap inside their own cell instead of overflowing the neighbour. **Status tokens are auto-colored** (exact match): rojo — Alta, High, Crítico, Vencido, Zombie; naranja — Media, Medium, Advertencia, Sobredimensionado, Saturado, Detection; verde — Baja, Low, OK, Correcto, Enabled, Prevention; azul — Informativo (env-degraded findings); "No aplica"/"No verificable" stay uncolored. |
| `Paragraph` | The block's `description` as prose. |

Empty data renders an italic "Sin datos disponibles."; unknown displayType renders a "Tipo de visualización no soportado" note.

## `data.json` (render input = merge)

```jsonc
{
  "meta": { "title", "companyName", "author", "subscriptionId", "generatedAt", "logoPath" },
  "branding": { "primaryColor", "secondaryColor", "panelColor" },
  "months": [...], "currency": "USD",
  "signals": { /* from usage-report.json */ },
  "narrative": { /* from narrative.json, or {} */ },
  "sections": [ /* usage-report sections, verbatim */ ]
}
```

Written for traceability/inspection; the body is generated in C# from the same model.

## Anti-patterns

- **Editing `main.typ` / `report.typ` by hand** — regenerated on every run; change the embedded templates in `render-typst.cs`.
- **Computing aggregations in Typst** — stage 9 owns all math; the renderer formats pre-computed numbers.
- **Narrative markup** — narrative strings are literal text; `*`/`_` are escaped, never rendered as emphasis.
- **Growing the report** — resist adding per-resource dumps or per-block analysis; compactness is the product.
