---
name: typst-diagrams
description: Author node-edge diagrams (architecture, topology, flows) in Typst with the fletcher package, for PDF/print deliverables. Owns the generic fletcher mechanics — diagram/node/edge/enclose recipes, local image embedding in nodes (Typst has NO network access), sizing, traps, and the compile checklist. Provider-agnostic — it does not know about Azure or any icon catalog; icon/image obtention is the caller's job (e.g. `byteaid-assets-icons` for Azure icons, or any local SVG/PNG). Consumed by `azure-diagrams` as its Typst render target and available standalone. NOT for markdown-embedded diagrams (mermaid), Visio files (`vsdx-diagrams`), or data-visualization charts.
when_to_use: |
  - Authoring or editing a diagram inside a Typst document (proposal, report, any PDF/print deliverable).
  - Any task mentioning fletcher, Typst diagram, node/edge diagram in Typst, embedding images/icons in a Typst diagram.
  - Invoked by `azure-diagrams` (and transitively by proposal/report generators) for their Typst diagrams.
  - NOT for choosing between render targets by consumption surface (domain layers such as `azure-diagrams` own that), and NOT for resolving/downloading icons.
allowed-tools: Bash, PowerShell, Read, Write
user-invocable: false
---

# Typst Diagrams (fletcher)

L1 index. Generic rules for node-edge diagrams in Typst/`fletcher`; recipes and traps live in [fletcher.md](fletcher.md).

## Non-negotiable rules

1. **Every referenced image must exist on disk before compiling.** Typst cannot fetch URLs — `#image("https://…")` fails. Download assets first (with `curl -sf` so an HTTP error aborts loudly instead of writing an error page into the file); commit them beside the `.typ` so compilation is reproducible offline.
2. **When a node carries an image, the image IS the node.** Set `node-stroke: none` / `node-fill: none` at the diagram level — a drawn border duplicates the image's own silhouette. Reserve stroke/fill for `enclose:` grouping nodes and for image-less nodes (actors, external systems).
3. **Edges carry short labels (protocol/port/action); nodes carry the concept.** Never put an image on an edge.
4. **Compile to verify.** A diagram is done when `typst compile` exits `0`. When the `typst` CLI is unavailable, state that compilation is unverified.

## Dispatch

| Need | Read |
|---|---|
| fletcher recipe — import, helpers, node/edge/enclose, image-in-node, sizing, traps, checklist | [fletcher.md](fletcher.md) |

## Cross-references

- `azure-diagrams` — Azure layer: icon slug discipline, target selection, Azure sizing conventions.
- `vsdx-diagrams` — Visio render target for the same topology.
- A host procedure may mandate its own asset layout (e.g. a proposal generator requiring `.assets/icons/`) — follow the host's layout and adjust paths in the recipes.
