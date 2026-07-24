# VSDX Diagram Spec (JSON)

**Version:** v1.1.0
**Updated:** 2026-07-23

L2 leaf. Input contract of [scripts/json-to-vsdx.cs](scripts/json-to-vsdx.cs). Property names are case-insensitive; comments and trailing commas tolerated. Units: **inches**, origin **bottom-left**.

## Shape

```jsonc
{
  "title": "Sample Topology",        // optional — page display name
  "pageWidth": 11,                   // optional — else computed to fit content
  "pageHeight": 8.5,
  "nodes": [
    {
      "id": "aca",                   // required, unique — referenced by edges/groups
      "label": "Container Apps",     // optional — defaults to id; \n allowed for line breaks
      "x": 2, "y": 5,                // optional PAIR — center position; omit on ANY node => full auto-layout
      "w": 1.6, "h": 1.0,            // optional — defaults 1.6 x 1.0
      "shape": "rounded",            // rectangle (default) | rounded | ellipse — ignored when image is set
      "fill": "#dae8fc",             // optional #RRGGBB — default white; ignored when image is set
      "line": "#404040",             // optional border color; ignored when image is set
      "fontSize": 10,                // optional pt — default 10 (9 for image nodes)
      "image": "icons/service.png"   // optional — embeds the raster as the node (png|jpg|jpeg|gif)
    }
  ],
  "edges": [
    {
      "from": "aca", "to": "sql",    // required — node ids
      "label": "TDS 1433",           // optional — keep short; carries protocol/port
      "arrow": true,                 // default true — arrowhead at "to" end
      "dashed": false,               // default false
      "line": "#404040",             // optional color
      "fontSize": 8                  // optional pt — default 8
    }
  ],
  "groups": [
    {
      "label": "VNet prod",          // optional — rendered top-centered
      "members": ["agw", "aca"],     // node ids — enclosure = members bbox + inset
      "line": "#7f7f7f"              // optional dash color
    }
  ]
}
```

## Rules

- `x`/`y` are all-or-nothing: if a single node omits them, the auto-layout recomputes EVERY position (layered left→right following edge direction, columns vertically centered). Mixing manual and auto placement is not supported.
- Edges carry protocol/port text; nodes carry the concept. Keep edge labels ≤ ~20 chars — the text sits on the line and rotates with it.
- Groups do not nest and do not move members — they are drawn enclosures (dashed, unfilled, behind members), not Visio containers.
- Colors are `#rrggbb` only (no named colors, no alpha).

## Image nodes

- `image` embeds the file as a Visio Foreign object: **the image IS the node** — no border/fill drawn, label rendered BELOW the image. Default size `0.6 × 0.6` in; override with `w`/`h`.
- Paths resolve relative to the process working directory — run the script from the spec's folder or use absolute paths.
- **Raster only** (`png|jpg|jpeg|gif`). The VSDX format cannot carry SVG as an embedded picture (Visio vectorizes imported SVGs into native shapes) — the script rejects `.svg` with exit `2`. Rasterize first, e.g. with Typst:
  ```
  #set page(width: auto, height: auto, margin: 0pt)
  #image("icon.svg", width: 96pt)
  ```
  then `typst compile icon.typ icon.png --ppi 144` and reference the PNG.
- The same file referenced by several nodes is embedded ONCE (deduplicated by full path).
