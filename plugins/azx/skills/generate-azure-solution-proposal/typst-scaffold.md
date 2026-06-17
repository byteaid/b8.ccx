# Typst Scaffold & Style Guide Contract — Azure Solution Proposal

**Version:** v1.0.0
**Updated:** 2026-06-04

L2 leaf. The fixed Typst skeleton and the ONLY customization surface (the `theme` dict). Structure is frozen by [sections.md](sections.md); a style guide may change appearance, never anatomy.

## File layout (fixed)

```
{output-dir}/
  main.typ              # the document — scaffold below
  .assets/              # EVERYTHING downloaded at generation time lands here
    icons/*.svg         #   service icons per byteaid-assets-icons (curl -sf)
    *                   #   any other fetched resource (images, data files)
  .styles/              # optional user-provided branding (copied verbatim)
    theme.typ           #   theme overrides
    logo.png|svg        #   logo assets
    fonts/*.ttf|otf     #   brand font FILES — fonts are never assumed installed
  proposal.pdf          # compiled output
```

Rule: downloaded resources → `.assets/`; user-provided branding → `.styles/`; never mix the two, never scatter files at the root.

## Theme contract

All visual knobs live in one `theme` dict at the top of `main.typ`. Defaults:

```typst
#let theme = (
  primary: rgb("#0b556a"),      // headings, title
  accent: rgb("#3c7a96"),       // group strokes, highlights
  muted: rgb("#5a6b76"),        // subtitles, captions, roles
  table-stroke: rgb("#c8d2d8"),
  band: rgb("#eef6f9"),         // table group bands
  text-font: "Libertinus Serif", // Typst-BUNDLED — identical output on any machine with zero installs
  base-size: 10pt,
  logo: none,                   // path to client/provider logo image, or none
  company: none,                // provider name for the footer, or none
  client: none,                 // client name shown under the title, or none
  paper: "a4",
)
```

**Style guide = the `.styles/` directory (deterministic merge):** branding enters the document EXCLUSIVELY through a user-provided `.styles/` directory:

```
.styles/
  theme.typ        # optional — #let overrides = (company: "...", primary: rgb("..."), ...)
  logo.png|svg     # optional — referenced by theme.typ as logo: "logo.png"
  fonts/*.ttf|otf  # optional — REQUIRED when theme.typ names a non-bundled font
  *                # any other assets theme.typ references
```

Merge rule: known key → override; unknown key → ignore and tell the user; missing key → default. The whole `.styles/` directory is copied into `{output-dir}/.styles/` so compilation is self-contained (`logo:` paths resolve as `".styles/" + value`). A style guide can NEVER add/remove/reorder sections, change table columns, or alter diagram content — if it asks to, refuse that part and say why.

**Fonts (never assume local installs):** fonts resolve from exactly two sources — the Typst-bundled set (Libertinus Serif, New Computer Modern, DejaVu Sans Mono) or font FILES shipped in `.styles/fonts/`. System-installed fonts are NOT a valid source: a document that compiles only because the author's machine has the font is a reproducibility defect. A `theme.typ` that sets `text-font` to a non-bundled family MUST ship its `.ttf`/`.otf` files (all weights used: regular, bold, italic) in `.styles/fonts/`; reject the override otherwise and fall back to the default, telling the user which files are missing.

**Whitelabel invariant (hard):** the deliverable is brand-neutral by default. With no `.styles/`, the defaults apply: `company: none`, `logo: none`, neutral palette — and the document carries NO company names, logos, or tooling credits of any kind. In particular, never mention ByteAid or `assets.byteaid.io` (the icon source is internal tooling, not document content). All branding — including any ByteAid branding — appears only when the supplied `.styles/` carries it. Citing Microsoft sources for data provenance (e.g. "Azure Retail Prices API" in the cost header) is factual attribution, not branding, and stays.

## Scaffold (fill `{...}` placeholders; keep everything else verbatim)

````typst
#import "@preview/fletcher:0.5.7": diagram, node, edge

// ── theme (defaults + style-guide overrides) ──
#let theme = ( /* merged dict per the contract above */ )

#set page(paper: theme.paper, margin: (x: 1.6cm, y: 1.8cm), numbering: "1",
  footer: context [#text(size: 8pt, fill: theme.muted)[
    #if theme.company != none [#theme.company — ] Propuesta de solución · #counter(page).display()]])
#set text(font: theme.text-font, size: theme.base-size)
#set heading(numbering: "1.")
#show heading: set text(fill: theme.primary)
#set table(stroke: 0.4pt + theme.table-stroke, inset: 5pt)
#show table.cell.where(y: 0): set text(weight: "bold", size: 8.5pt)

// ── service icon helpers ──  (whitelabel: no tooling names in generated sources)
#let azicon(slug, size: 18pt) = image(".assets/icons/" + slug + ".svg", width: size)
#let svc(slug, name, role: none) = align(center)[
  #azicon(slug, size: 26pt) \
  #text(weight: "bold", size: 8pt)[#name]
  #if role != none [ \ #text(size: 7pt, style: "italic", fill: theme.muted)[#role] ]
]

// ── title block ──
#align(center)[
  #if theme.logo != none [#image(theme.logo, height: 1.4cm) #v(2pt)]
  #text(size: 17pt, weight: "bold")[{Project title} — Propuesta de solución en Azure]\
  #text(size: 11pt, fill: theme.muted)[#if theme.client != none [{client} · ] v{1.0.0} · {YYYY-MM-DD}]
]
#v(4pt)

= Resumen ejecutivo
{per sections.md § 1 — written last}

= Antecedentes
{per sections.md § 2}

= Propuesta de solución
{per sections.md § 3}

= Arquitectura
// fletcher conventions (icon-is-the-node, enclose groupings, edge labels, traps) per `azure-diagrams`
#figure(
  diagram(
    spacing: (14mm, 9mm),
    node-stroke: none,          // icons stand directly — azure-diagrams rule (icon IS the node)
    node-fill: none,
    edge-stroke: 0.6pt + theme.accent.darken(20%),
    label-size: 6.5pt,
    // groupings: node(enclose: (...), stroke: (dash: "dashed", paint: theme.accent), inset: 9pt, snap: false)
    // services:  node((x,y), svc("{slug}", "{Service}", role: "{role}"), name: <id>)
    // actors:    node((x,y), [Cliente], stroke: 0.5pt + theme.table-stroke, fill: theme.band, corner-radius: 10pt)
    // edges:     edge(<a>, <b>, "->", [HTTPS 443])  // "-->" failover, "=>" platform replication
  ),
  caption: [{one neutral line describing the topology — no tooling or provider credits}],
)
{resource-role table per sections.md § 4 — first column uses #azicon("{slug}") {Name}}

= Costo de infraestructura
{quote table per sections.md § 5; escape dollars as \$ in prose}

= Esfuerzo
{task table per sections.md § 6}

= Cronograma
{intro line: total hours · capacity · N business days · start → end}
#import "@preview/timeliney:0.4.0"
#timeliney.timeline(
  show-grid: true,
  {
    import timeliney: *
    headerline(group(([*Semana 1*], 1)), group(([*Semana 2*], 1)) /* …one group per week… */)
    headerline(group(([#text(size: 7pt)[{DD–DD mon}]], 1)) /* …matching date ranges… */)
    taskgroup(title: [*{Fase}*], {
      task([{Tnn Tarea}], ({(a-1)/5}, {b/5}), style: (stroke: 2.5pt + theme.accent))
      // one task() per § 6 row, grouped by phase
    })
    milestone(at: {d/5}, style: (stroke: (dash: "dashed", paint: theme.muted)),
      align(center, text(size: 7pt)[*{Hito}*\ {YYYY-MM-DD}]))
    // one milestone() per milestone-table row — same set
  }
)
{milestones table per sections.md § 7: Hito / Día / Fecha}

= Pre requisitos y supuestos
{per sections.md § 8}

= Fuera de alcance
{per sections.md § 9}
````

## Compile & verify (every run)

```bash
cd {output-dir}
FONTS="--ignore-system-fonts"                            # reproducible: bundled + .styles/fonts only
[ -d .styles/fonts ] && FONTS="$FONTS --font-path .styles/fonts"
typst compile $FONTS main.typ proposal.pdf               # MUST exit 0 AND zero font warnings
typst compile $FONTS main.typ "preview-{p}.png" --format png --ppi 110
# visually inspect every preview-N.png, then delete them
```

`--ignore-system-fonts` is mandatory — it is what makes the compile fail loudly (font warning) on a machine-dependent font instead of silently producing a different-looking PDF elsewhere. Any `unknown font family` warning is a gate failure: fix the theme or ship the files in `.styles/fonts/`.

Visual checklist: icons render directly (no enclosing boxes), diagram fits its page without overlaps, no table overflows the text width, theme colors applied, footer + page numbers present. Known traps: fletcher `shape:` takes a function not a string (use `corner-radius`); a missing `icons/*.svg` aborts compilation — re-run the download step, don't hand-edit the diagram around it; literal `$` in text must be `\$` or Typst enters math mode; node labels in Spanish (or any verbose language) widen fletcher nodes and can push the diagram off-page — keep `role:` strings short (≤ \~30 chars), reduce `spacing` before reducing font, and nudge colliding edge labels with `label-pos:` instead of moving nodes; in the Gantt, adjacent `milestone()` labels collide when marks are < \~0.4 week units apart — keep milestone label text to two short lines and thin the milestone set (phases already narrate progress) rather than shrinking the font below 7pt; timeliney ≤ 0.2.0 pins a cetz incompatible with typst ≥ 0.13 and panics with `Failed to resolve coordinate: (0, 0)` — use `timeliney:0.4.0`.
