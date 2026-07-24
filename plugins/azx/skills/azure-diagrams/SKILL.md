---
name: azure-diagrams
description: Azure composition layer for architecture/topology diagrams — picks the render target by consumption surface (mermaid for docs sites, `typst-diagrams` for PDF/print, `vsdx-diagrams` for Visio deliverables) and owns the Azure icon discipline (the icon IS the node, one resolved slug per service, standard sizes). Consumes `byteaid-assets-icons` for slug resolution/verification/download — this skill never invents slugs — and `typst-diagrams` (dgx) for the generic fletcher mechanics. Reused by `generate-azure-solution-proposal` (the Arquitectura diagram) and available standalone for any Azure topology diagram.
when_to_use: |
  - Creating or editing an architecture / topology diagram that should carry Azure service icons.
  - Choosing a diagram render target for a given consumption surface (GitHub markdown vs PDF vs docs site vs Visio).
  - Any task mentioning Azure architecture diagram, topology diagram, mermaid flowchart with icons, "icon is the node".
  - Invoked by `generate-azure-solution-proposal` for its § Arquitectura diagram. NOT for resolving an icon slug (that is `byteaid-assets-icons`), NOT for generic fletcher mechanics (`typst-diagrams`), and NOT for data-visualization charts (bar/line/pie).
allowed-tools: Bash, PowerShell, Read, Write, WebFetch
user-invocable: false
---

# Azure Architecture Diagrams

L1 index. How to compose **resolved** Azure service icons into an architecture diagram, per render target. Slug resolution, verification, and download are NOT this skill's job — `byteaid-assets-icons` owns that; resolve + verify + download every icon there FIRST, then author the diagram.

## Target selection

| Consumption surface | Target | Why |
|---|---|---|
| Docs site, VS Code preview, mkdocs-material, embedded mermaid.js | **mermaid** | External `<img>` in labels renders by default. |
| PDF / print deliverable (proposal, report) | **Typst** via `typst-diagrams` | High-fidelity, offline, paginated; icons embedded from local files. |
| Editable Visio deliverable (.vsdx) | **`vsdx-diagrams`** | Native Visio file from a JSON spec, icons included — reference the downloaded SVGs directly; the script auto-rasterizes them (typst CLI). |
| **GitHub.com markdown** | Typst, or pre-rendered image | GitHub sanitizes external images inside mermaid — icons silently vanish. |

## Non-negotiable rules (all targets)

1. **The icon IS the node.** Suppress any enclosing box/circle around an icon-bearing node. Reserve shapes ONLY for groupings (regions, zones) and for icon-less actors (users, external SaaS).
2. **Resolve before authoring.** Every Azure node's slug is resolved + verified `200` + (for Typst) downloaded via `byteaid-assets-icons` this session. One slug per service across the WHOLE artifact — never mix `app-services` and `app-service-plans` for the same node.
3. **Edges carry protocol/port; nodes carry icons.** Label edges with protocol/port (`HTTPS 443`, `TDS 1433`, `HTTPS / SAS`); never put an icon on an edge.
4. **Pick the target by consumption surface** (table above) before drawing — a mermaid diagram bound for GitHub.com loses its icons.
5. **Standard sizes.** Source viewBox is `0 0 18 18` (scales cleanly). Mermaid node icon `width='36'` (`24` dense); Typst `28pt` diagram node, `18pt` inline.

## Dispatch

| Need | Read |
|---|---|
| Mermaid recipe — `<img>` in the node label, conventions, renderer caveats (incl. GitHub stripping) | [mermaid.md](mermaid.md) |
| Typst — Azure deltas over the generic recipe (`azicon`/`svc` helpers, download discipline, host layouts) | [typst-fletcher.md](typst-fletcher.md) |
| Visio — spec authoring and generation | `vsdx-diagrams` § spec (image nodes reference the downloaded SVGs; label = service name + role) |

## Cross-references

- `byteaid-assets-icons` — icon slug resolution, verification, download (run BEFORE authoring any diagram here).
- `typst-diagrams` — generic fletcher mechanics (skeleton, enclose, traps, compile checklist); this skill only adds the Azure layer on top.
- `vsdx-diagrams` — JSON spec → native `.vsdx` via bundled script.
- A host procedure may mandate its own asset layout (e.g. `generate-azure-solution-proposal` requires downloaded icons under `.assets/icons/` and forbids tooling names in generated sources) — follow the host's layout and adjust the paths in these recipes accordingly.
