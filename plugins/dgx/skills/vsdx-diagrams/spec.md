# VSDX Diagram Spec (JSON)

**Version:** v1.0.0
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
      "shape": "rounded",            // rectangle (default) | rounded | ellipse
      "fill": "#dae8fc",             // optional #RRGGBB — default white
      "line": "#404040",             // optional border color
      "fontSize": 10                 // optional pt — default 10
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
