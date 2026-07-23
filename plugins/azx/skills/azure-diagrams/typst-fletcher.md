# Typst — Azure Deltas over `typst-diagrams`

**Version:** v1.1.0
**Updated:** 2026-07-23

L2 leaf. Azure-specific additions for the Typst render target. The generic fletcher mechanics — import, diagram skeleton, `enclose:` groupings, traps, compile checklist — live in `typst-diagrams` § fletcher; read that first. This file only adds the Azure icon layer.

## Download (defer to `byteaid-assets-icons`)

Typst has no network access — resolve, verify, and download each SVG via `byteaid-assets-icons` into an asset folder next to the `.typ` file BEFORE authoring. The helpers below use `icons/`; a host procedure may mandate its own layout (e.g. `generate-azure-solution-proposal` requires `.assets/icons/`) — follow the host's layout and adjust the paths. Always download with `curl -sf` so a bad slug aborts loudly instead of writing an HTML error page into the `.svg`.

## Azure helpers

```typst
// slug = the resolved byteaid-assets-icons slug (SKILL.md rule 2: one slug per service)
#let azicon(slug, size: 18pt) = image("icons/" + slug + ".svg", width: size)

// inline next to a service name
#azicon("storage-accounts") Storage Account

// as a labeled node block for fletcher nodes, tables, grids
#let svc(slug, name, role: none) = align(center)[
  #azicon(slug, size: 28pt) \
  #text(weight: "bold", size: 9pt)[#name]
  #if role != none [ \ #text(size: 8pt, style: "italic")[#role] ]
]
```

Pass `svc(...)` as the fletcher node body, with `node-stroke: none` / `node-fill: none` at the diagram level (the icon IS the node — [SKILL.md](SKILL.md) rule 1; skeleton in `typst-diagrams` § fletcher):

```typst
#diagram(
  node-stroke: none,
  node-fill: none,
  node((0,0), svc("container-apps-environments", "Container Apps", role: "api"), name: <aca>),
  node((2,0), svc("azure-sql", "Azure SQL"), name: <sql>),
  edge(<aca>, <sql>, "->", [TDS 1433]),
)
```

## Azure conventions

- Standard sizes: `18pt` inline, `28pt` diagram node ([SKILL.md](SKILL.md) rule 5).
- Non-Azure actors (users, external SaaS) go icon-less with a neutral shape; every Azure service node carries its icon.
- A missing `icons/*.svg` means the download step was skipped or a slug is wrong — re-run the `byteaid-assets-icons` procedure; never hand-edit the diagram around a missing icon.
- Commit the SVGs with the document so compilation is reproducible offline.
