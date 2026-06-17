# Typst `fletcher` — Download First, Then `#image`

**Version:** v1.0.0
**Updated:** 2026-06-17

L2 leaf. Composing resolved Azure service icons into a Typst `fletcher` diagram. Slug resolution + verification + download live in `byteaid-assets-icons`; the diagram-level rules live in [SKILL.md](SKILL.md). Typst has **no network access** — every SVG MUST already be on disk before authoring.

## Download (defer to `byteaid-assets-icons`)

`#image("https://...")` fails — Typst cannot fetch URLs. Resolve, verify, and download each SVG via `byteaid-assets-icons` into an asset folder next to the `.typ` file. The examples below use `icons/`; a host procedure may mandate its own layout (e.g. `generate-azure-solution-proposal` requires `.assets/icons/`) — follow the host's layout and adjust the paths. Always download with `curl -sf` (fail on HTTP error) so a bad slug aborts loudly instead of writing an HTML error page into the `.svg` — Typst's error for a corrupt SVG is cryptic.

## Helpers

```typst
// one helper per document; slug = the resolved byteaid-assets-icons slug
#let azicon(slug, size: 18pt) = image("icons/" + slug + ".svg", width: size)

// inline next to a service name
#azicon("storage-accounts") Storage Account

// as a labeled node block (fletcher nodes, tables, grids)
#let svc(slug, name, role: none) = align(center)[
  #azicon(slug, size: 28pt) \
  #text(weight: "bold", size: 9pt)[#name]
  #if role != none [ \ #text(size: 8pt, style: "italic")[#role] ]
]
```

## Diagram

Pass `svc(...)` as the fletcher node body. **The icon IS the node — never draw a box/circle around it** ([SKILL.md](SKILL.md) rule 1). Set `node-stroke: none` and `node-fill: none` at the diagram level so each SVG stands directly with its label below. Reserve stroke/fill for `enclose:` grouping nodes (regions, zones) and for icon-less actors (users, external SaaS):

```typst
#import "@preview/fletcher:0.5.7": diagram, node, edge
#diagram(
  node-stroke: none,            // icons stand directly — no enclosing shape
  node-fill: none,
  node((0,0), svc("container-apps-environments", "Container Apps", role: "api"), name: <aca>),
  node((2,0), svc("azure-sql", "Azure SQL"), name: <sql>),
  node((4,0), [Client], stroke: 0.5pt, fill: rgb("#f7fafb"), corner-radius: 10pt, name: <user>),  // icon-less actor keeps a shape
  // grouping: node(enclose: (<aca>, <sql>), stroke: (dash: "dashed"), inset: 9pt, snap: false)
  edge(<aca>, <sql>, "->", [TDS 1433]),    // "-->" failover, "=>" platform replication
)
```

Conventions:

- `icons/` lives beside the `.typ`; commit the SVGs with the document so compilation is reproducible offline.
- Standard sizes: `18pt` inline, `28pt` diagram node.
- Groupings (regions/zones) are dashed `enclose:` nodes around member node names; edges carry protocol/port ([SKILL.md](SKILL.md) rule 3).

## fletcher traps

- `shape:` takes a **function, not a string** — `shape: "pill"` is a compile error; use `corner-radius`, or import `fletcher.shapes`.
- A missing `icons/*.svg` aborts compilation — re-run the download step per `byteaid-assets-icons`; never hand-edit the diagram around a missing icon.
- Verbose node labels (Spanish/long roles) widen nodes and can push the diagram off-page — keep `role:` strings short (≤ ~30 chars), reduce `spacing` before reducing font, and nudge colliding edge labels with `label-pos:` instead of moving nodes.
- Literal `$` in any text must be `\$` or Typst enters math mode.

## Checklist

1. Every Azure service node has an icon; non-Azure actors may go icon-less or use a neutral shape.
2. Every slug was resolved + verified `200` via `byteaid-assets-icons` this session (one slug per service across the artifact).
3. All referenced files exist under the icon folder and compile (`typst compile` exits 0). When the `typst` CLI is unavailable, state that compilation is unverified.
