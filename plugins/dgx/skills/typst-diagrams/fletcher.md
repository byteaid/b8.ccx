# fletcher Recipe

**Version:** v1.0.0
**Updated:** 2026-07-23

L2 leaf. Concrete fletcher authoring: helpers, diagram skeleton, groupings, traps, checklist. Generic rules live in [SKILL.md](SKILL.md).

## Import

```typst
#import "@preview/fletcher:0.5.7": diagram, node, edge
```

Pin the version — `@preview` packages are immutable per version; an unpinned import breaks reproducibility.

## Helpers

```typst
// one pair of helpers per document; folder holds pre-downloaded assets (SKILL.md rule 1)
#let icn(name, size: 18pt) = image("icons/" + name + ".svg", width: size)

// labeled node block: image on top, bold name, optional italic role
#let blk(name, title, role: none) = align(center)[
  #icn(name, size: 28pt) \
  #text(weight: "bold", size: 9pt)[#title]
  #if role != none [ \ #text(size: 8pt, style: "italic")[#role] ]
]
```

## Diagram skeleton

```typst
#diagram(
  node-stroke: none,            // image-bearing nodes stand directly — no enclosing shape
  node-fill: none,
  node((0,0), blk("service-a", "Service A", role: "api"), name: <a>),
  node((2,0), blk("database", "Database"), name: <db>),
  node((4,0), [Client], stroke: 0.5pt, fill: rgb("#f7fafb"), corner-radius: 10pt, name: <user>),  // image-less node keeps a shape
  node(enclose: (<a>, <db>), stroke: (dash: "dashed"), inset: 9pt, snap: false),                  // grouping enclosure
  edge(<a>, <db>, "->", [TCP 5432]),   // "-->" dashed (failover/async), "=>" double (replication/bulk)
)
```

Conventions:

- Grid coordinates `(col, row)` — keep nodes on integer positions and let `spacing` breathe; nudge with fractional coords only to resolve collisions.
- Groupings (regions, zones, boundaries) are dashed `enclose:` nodes listing member node names.
- Default sizes when the caller does not mandate its own: `18pt` inline image, `28pt` diagram-node image.

## Traps

- `shape:` takes a **function, not a string** — `shape: "pill"` is a compile error; use `corner-radius`, or import `fletcher.shapes`.
- A missing asset file aborts compilation with a cryptic error — verify every referenced file exists before compiling; never hand-edit the diagram around a missing image.
- Verbose labels widen nodes and can push the diagram off-page — keep `role:` strings short (≤ ~30 chars), reduce `spacing` before reducing font, and nudge colliding edge labels with `label-pos:` instead of moving nodes.
- Literal `$` in any text must be `\$` or Typst enters math mode.
- `enclose:` nodes need `snap: false` or edges glue to the enclosure instead of the member nodes.

## Checklist

1. All referenced assets exist on disk under the asset folder.
2. Image-bearing nodes have no stroke/fill; groupings are dashed enclosures; edges carry short labels.
3. `typst compile` exits `0` (state "compilation unverified" when the CLI is unavailable).
