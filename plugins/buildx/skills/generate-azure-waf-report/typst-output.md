# Typst Output — Template Architecture

`render-typst` writes a self-contained Typst project under `{stage-dir}/typst/`. The project compiles standalone (`typst compile main.typ output.pdf`) and reads all of its data from `data.json` via Typst's `json()` builtin. No script logic lives inside the templates.

## Inspiration

The template skeleton mirrors the upstream `ByteAid.CloudAnalyzer.Export.Typst.Templates` set (`main.typ`, `cover.typ`, `executive-summary.typ`, `analysis-section.typ`, `conclusion.typ`, `theme.typ`, `trend-table.typ`, `charts/`). The differences here are:
- One Typst file per WAF pillar (`pillar-{id}.typ`) instead of a flat sequence of analysis sections.
- A fixed `data.json` schema independent of the upstream `AnalysisCompletedResult` C# type.
- No reliance on the upstream Markdown-to-Typst converter — the renderer either ships pre-rendered Typst content or accepts plain text.

## File layout

```
{stage-dir}/typst/
  main.typ                    # entry point
  theme.typ                   # primary/secondary/accent colors, fonts
  cover.typ                   # title page
  executive-summary.typ       # cross-pillar summary
  pillar-reliability.typ
  pillar-security.typ
  pillar-cost-optimization.typ
  pillar-operational-excellence.typ
  pillar-performance-efficiency.typ
  pillar-sustainability.typ
  conclusion.typ              # closing notes
  charts/
    bar-chart.typ
    line-chart.typ
    pie-chart.typ
    trend-table.typ
  data.json                   # single source of truth for the templates
  logo.png                    # optional, only when --logo supplied
```

## `data.json` shape

```json
{
  "meta": {
    "title": "Azure WAF Assessment — Contoso Production",
    "companyName": "Contoso",
    "logoPath": "logo.png",
    "hideCompanyName": false,
    "generatedAt": "2026-04-28 14:00:00 UTC",
    "subscriptionId": "00000000-...",
    "period": { "start": "2026-01-01", "end": "2026-04-28" }
  },
  "branding": {
    "primaryColor": "#1b6ec2",
    "secondaryColor": "#6c757d",
    "accentColor": "#0dcaf0"
  },
  "executiveSummary": "Plain text or pre-rendered Typst markup.",
  "pillars": [
    {
      "id": "cost-optimization",
      "name": "Cost Optimization",
      "summary": "...",
      "reportSets": [
        {
          "id": "cost-trend-monthly",
          "title": "Monthly Cost Trend",
          "displayType": "LineChart",
          "description": "...",
          "chart": {
            "xLabel": "Month",
            "yLabel": "USD",
            "showGrid": true,
            "series": [
              { "name": "Total", "color": "#1b6ec2",
                "points": [ { "label": "2026-01", "value": 1234.5 } ] }
            ],
            "referenceLines": []
          },
          "pie": null,
          "trendTable": null
        }
      ]
    }
  ],
  "conclusion": "Plain text or pre-rendered Typst markup."
}
```

`displayType` ∈ `Paragraph | BarChart | LineChart | PieChart | TrendTable`. Exactly one of `chart`, `pie`, `trendTable` is non-null per report set, matching `displayType`.

## `main.typ` skeleton

```typst
#import "theme.typ": *

#let data = json("data.json")

#set page(paper: "us-letter", margin: (top: 2.5cm, bottom: 2.5cm, left: 2cm, right: 2cm))
#set text(font: body-font, size: 10pt)
#set par(justify: true, leading: 0.65em)

#import "cover.typ": render-cover
#render-cover(data)

#pagebreak()

#import "executive-summary.typ": render-executive-summary
#render-executive-summary(data)

#for pillar in data.pillars {
  let path = "pillar-" + pillar.id + ".typ"
  include path
}

#import "conclusion.typ": render-conclusion
#render-conclusion(data)
```

## `theme.typ` skeleton

```typst
#let data = json("data.json")
#let primary = rgb(data.branding.primaryColor)
#let secondary = rgb(data.branding.secondaryColor)
#let accent = rgb(data.branding.accentColor)

#let body-font = "New Computer Modern"
#let heading-font = "New Computer Modern Sans"
```

Renderer guarantees the colors in `data.branding` are valid CSS-style hex strings; `theme.typ` does no validation.

## Charts

Stage 7 does NOT compute pixel-perfect charts inside Typst — it emits the data and lets the templates draw native Typst plots via the `charts/` helpers. Bar / line / pie charts are implemented with plain Typst primitives (`rect`, `line`, `circle`, `place`) keyed off the points list. Trend tables are implemented as styled Typst tables with arrow glyphs and conditional fill colors driven by `up/down/stableColor`.

## Compilation

The renderer prefers compiling itself when `--compile` is set and `typst` is on PATH (`typst --version` succeeds):

```
typst compile main.typ ../output.pdf
```

When compilation is skipped, the renderer prints to stderr:

```
typst project staged at: {stage-dir}/typst/
to compile manually:    typst compile main.typ output.pdf
```

The operator can also use `typst watch main.typ output.pdf` for iterative editing of the templates.

## Adding a new pillar or section

1. Extend the `report-sets.json` contract in stage 6 (`pipeline.md`) so the pillar appears with at least one `ReportSet`.
2. Add a `pillar-{id}.typ` file to the renderer's bundled template set.
3. Update `main.typ`'s `for pillar in data.pillars` loop — no change needed if the loop is the dynamic `include` form above.
4. Document the new pillar's data sources in this skill's L1 dispatch table.

## Anti-patterns

- **Embedding C# strings of Typst markup in `report-sets.json`.** Keep the JSON declarative; Typst markup belongs in the bundled templates.
- **Computing percentages or aggregations inside Typst.** Stage 6 owns aggregation; the templates render numbers as-is.
- **Branching template behaviour on the subscription ID.** Branding/meta is the only template-side knob.
- **Calling `typst compile` from anywhere except `render-typst`.** Other stages have no business shelling out to Typst.
