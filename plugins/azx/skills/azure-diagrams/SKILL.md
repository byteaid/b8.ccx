---
name: azure-diagrams
description: Authoring architecture diagrams that carry Azure service icons, in two render targets — mermaid (HTML `<img>` in the node label) and Typst/`fletcher` (local `#image`). Covers the target-agnostic conventions (the icon IS the node, no enclosing box; groupings as dashed enclosures; edges carry protocol/port; sizing) and per-target recipes + caveats. Consumes `byteaid-assets-icons` for slug resolution/verification/download — this skill never invents slugs, it composes already-resolved icons into a diagram. Reused by `generate-azure-solution-proposal` (the Arquitectura diagram) and available standalone for any topology diagram.
when_to_use: |
  - Creating or editing an architecture / topology diagram (mermaid or Typst) that should carry Azure service icons.
  - Choosing a diagram render target for a given consumption surface (GitHub markdown vs PDF vs docs site).
  - Any task mentioning architecture diagram, topology diagram, mermaid flowchart with icons, Typst fletcher diagram, "icon is the node".
  - Invoked by `generate-azure-solution-proposal` for its § Arquitectura diagram. NOT for resolving an icon slug (that is `byteaid-assets-icons`) and NOT for data-visualization charts (bar/line/pie).
allowed-tools: Bash, PowerShell, Read, Write, WebFetch
user-invocable: false
---

# Azure Architecture Diagrams

L1 index. How to compose **resolved** Azure service icons into an architecture diagram, in two render targets. Slug resolution, verification, and download are NOT this skill's job — `byteaid-assets-icons` owns that; resolve + verify + download every icon there FIRST, then author the diagram here.

## Target selection

| Consumption surface | Target | Why |
|---|---|---|
| Docs site, VS Code preview, mkdocs-material, embedded mermaid.js | **mermaid** | External `<img>` in labels renders by default. |
| PDF / print deliverable (proposal, report) | **Typst/`fletcher`** | High-fidelity, offline, paginated; icons embedded from local files. |
| **GitHub.com markdown** | Typst, or pre-rendered image | GitHub sanitizes external images inside mermaid — icons silently vanish. |

## Non-negotiable rules (both targets)

1. **The icon IS the node.** Suppress any enclosing box/circle around an icon-bearing node — a drawn border duplicates the SVG's own silhouette and wastes space. Reserve shapes ONLY for `subgraph`/`enclose` groupings (regions, zones) and for icon-less actors (users, external SaaS).
2. **Resolve before authoring.** Every Azure node's slug is resolved + verified `200` + (for Typst) downloaded via `byteaid-assets-icons` this session. One slug per service across the WHOLE artifact — never mix `app-services` and `app-service-plans` for the same node.
3. **Edges carry protocol/port; nodes carry icons.** Label edges with protocol/port (`HTTPS 443`, `TDS 1433`, `HTTPS / SAS`); never put an icon on an edge.
4. **Pick the target by consumption surface** (table above) before drawing — a mermaid diagram bound for GitHub.com loses its icons.
5. **Standard sizes.** Source viewBox is `0 0 18 18` (scales cleanly). Mermaid node icon `width='36'` (`24` dense); Typst `28pt` diagram node, `18pt` inline.

## Dispatch

| Need | Read |
|---|---|
| Mermaid recipe — `<img>` in the node label, conventions, renderer caveats (incl. GitHub stripping) | [mermaid.md](mermaid.md) |
| Typst `fletcher` recipe — `azicon`/`svc` helpers, `node-stroke: none`, groupings via `enclose`, fletcher traps, compile checklist | [typst-fletcher.md](typst-fletcher.md) |

## Cross-references

- `byteaid-assets-icons` — icon slug resolution, verification, download (run BEFORE authoring any diagram here).
- A host procedure may mandate its own asset layout (e.g. `generate-azure-solution-proposal` requires downloaded icons under `.assets/icons/` and forbids tooling names in generated sources) — follow the host's layout and adjust the paths in these recipes accordingly.
