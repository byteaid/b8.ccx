# json-to-vsdx

**Version:** v1.1.0
**Updated:** 2026-07-23

Deterministic converter: JSON diagram spec → native Visio `.vsdx` (MS-VSDX, opens in Visio 2013+ and diagrams.net). Pure BCL .NET 10 file-based app — no NuGet packages, no Visio installation required to generate.

## Usage

```bash
dotnet run ${CLAUDE_SKILL_DIR}/scripts/json-to-vsdx.cs -- --input spec.json --output diagram.vsdx
```

| Arg | Required | Meaning |
|---|---|---|
| `--input` | yes | Path to the JSON diagram spec ([../spec.md](../spec.md)). |
| `--output` | yes | Path of the `.vsdx` to write. Overwritten if present. |
| `--ppi` | no | Rasterization density for SVG node images (default `192`). |

## Exit codes

| Code | Meaning |
|---|---|
| `0` | `.vsdx` written and self-checked (every XML part re-parsed). |
| `1` | Usage / IO error, or self-check failed. |
| `2` | Spec validation failed (no nodes, duplicate node ids, edge/group referencing an unknown node, unknown `shape`, missing/unsupported `image`) or SVG rasterization failed (`typst` missing from PATH / compile error). All violations listed on stderr. |

## Output

Stdout (data): one JSON line — `{"output","nodes","edges","groups","images","pageWidth","pageHeight"}`. Stderr: diagnostics only.

Determinism: same spec → byte-identical `.vsdx` (zip entry timestamps are fixed), so re-runs are idempotent and diffable by hash.

## Behavior notes

- Units are **inches**, origin **bottom-left** (Visio native). If any node lacks `x`/`y`, ALL positions are auto-computed (layered left→right by longest path from source nodes; cycle-safe).
- Page size: explicit `pageWidth`/`pageHeight`, else computed to fit content + margin.
- Connectors are 1-D shapes glued to source/target (`Connects` + `_WALKGLUE` formulas) — they survive moving shapes in Visio. Endpoints are pre-clipped at node borders so non-Visio viewers render them correctly too.
- Groups render as dashed enclosures behind their member nodes (bounding box + inset, label top-centered).
- Nodes with `image` (`svg|png|jpg|jpeg|gif`) embed it as a Foreign object — image IS the node, label below, media parts deduplicated by full path. Image paths resolve against the working directory. SVGs are auto-rasterized through the `typst` CLI at `--ppi` (the VSDX format cannot carry SVG); no typst on PATH → exit `2` ([../spec.md](../spec.md) § Image nodes).
- Not supported: multi-page documents, curved/right-angle routed connectors, masters/stencils, images on edges.
