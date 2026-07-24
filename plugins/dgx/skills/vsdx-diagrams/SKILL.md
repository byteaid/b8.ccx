---
name: vsdx-diagrams
description: Generate a native, editable Visio .vsdx diagram from a declarative JSON spec (nodes, edges, groups) via the bundled json-to-vsdx.cs .NET 10 script — pure BCL, no Visio and no third-party library required. Shapes (rectangle/rounded/ellipse), image nodes (embedded raster icons — png/jpg/gif; the image IS the node, label below), glued 1-D connectors with labels and arrows, dashed group enclosures, auto or explicit layout. Provider-agnostic: works for any topology/architecture/flow diagram, not just Azure. SVG sources must be rasterized first (recipe included). NOT for PDF/print output (that is `typst-diagrams`) nor markdown-embedded diagrams (mermaid).
when_to_use: |
  - The deliverable is a Visio file: .vsdx, "diagrama de Visio", "editable in Visio", a customer that works in Visio.
  - Exporting an already-designed topology/architecture/flow to .vsdx alongside other render targets.
  - Any task mentioning vsdx, Visio export, Visio drawing generation.
  - NOT for PDF/print diagrams (`typst-diagrams`), markdown/docs-site diagrams (mermaid), or data-visualization charts.
allowed-tools:
  - Bash(dotnet run ${CLAUDE_SKILL_DIR}/scripts/json-to-vsdx.cs *)
  - PowerShell(dotnet run ${CLAUDE_SKILL_DIR}/scripts/json-to-vsdx.cs *)
  - Read
  - Write
user-invocable: false
---

# VSDX Diagrams

L1 index. Producing a native Visio `.vsdx` is a 3-step deterministic procedure — author a JSON spec, run the bundled script, verify. Never hand-write VSDX XML; the script owns the file format.

## Procedure

1. **Prepare images (only if the diagram carries icons).** VSDX embeds raster only — rasterize any SVG icon to PNG first (Typst recipe in [spec.md](spec.md) § Image nodes) and place the PNGs beside the spec.
2. **Author the spec** — write `{name}.json` per [spec.md](spec.md). Prefer auto-layout (omit `x`/`y`) for flow-shaped diagrams; use explicit positions only when the consumer dictates placement. Icon nodes: set `image`, keep the label short.
3. **Run the script** — from the spec's folder (image paths resolve against cwd); see [scripts/json-to-vsdx.md](scripts/json-to-vsdx.md):
   `dotnet run ${CLAUDE_SKILL_DIR}/scripts/json-to-vsdx.cs -- --input {name}.json --output {name}.vsdx`
   Exit `2` = spec violation (all violations on stderr — fix the spec, re-run); exit `0` = written + self-checked.
4. **Verify** — trust exit `0` for structure. On Windows with Visio installed, optionally smoke-test visually: open via COM (`New-Object -ComObject Visio.InvisibleApp`, `Documents.Open`, `Page.Export("{name}.png")`) and inspect the PNG. State clearly when visual verification was not possible.

## Rules

1. **The spec is the source of truth.** Iterate on the JSON and regenerate — never patch the `.vsdx` (output is deterministic; a re-run is cheap and byte-stable).
2. **Edges carry protocol/port; nodes carry the concept.** Same discipline as every diagram target; keep edge labels short — they render on the line.
3. **When a node carries an image, the image IS the node** — the script draws no border/fill around it and hangs the label below; do not fake icon nodes with filled boxes when a raster is available.
4. Commit the spec JSON (and the rasterized icons it references) next to the generated `.vsdx` so the diagram is regenerable.

## Dispatch

| Need | Read |
|---|---|
| JSON input contract — nodes/edges/groups fields, layout rules, colors | [spec.md](spec.md) |
| Script args, exit codes, stdout contract, behavior notes | [scripts/json-to-vsdx.md](scripts/json-to-vsdx.md) |

## Cross-references

- `typst-diagrams` — PDF/print render target for the same topology.
- `azure-diagrams` — Azure-specific composition layer (icon discipline, target selection) that may hand off here for a Visio deliverable.
