---
name: byteaid-assets-icons
description: ByteAid Assets icons API (`https://assets.byteaid.io/api/icons/*`) — SVG service icons for architecture diagrams. Covers the discovery endpoints (categories / list / search), the icon-resolution workflow that maps an Azure service to its CORRECT icon slug, and the embedding recipes for mermaid (img-in-label) and Typst (download-then-#image, since Typst cannot fetch URLs). Currently exposes the `azure` category (~626 icons); the category model anticipates more.
when_to_use: |
  - Creating or editing an architecture diagram (mermaid or Typst) that should carry service icons.
  - Any task mentioning ByteAid Assets, assets.byteaid.io, diagram icons, Azure service icons, svgUrl, icon slug.
  - Resolving which icon represents a given Azure service, or verifying an icon URL.
  - Invoked by `generate-azure-solution-proposal` for the architecture diagram — every diagram node for an Azure service carries its icon.
allowed-tools: Bash, PowerShell, Read, Write, WebFetch
user-invocable: false
---

# ByteAid Assets — Diagram Icons

L1 index. Unauthenticated GET-only API serving SVG icons for architecture diagrams. Base: `https://assets.byteaid.io/api/icons`.

## Endpoints

| Endpoint | Returns | Notes |
|---|---|---|
| `GET /categories` | `[{id, displayName}]` | Currently `[{"id":"azure","displayName":"Azure"}]`. |
| `GET /list?page=N&pageSize=100` | Paged `{items, page, pageSize, totalItems, totalPages, hasNextPage, hasPreviousPage}` | Default `pageSize=20`, **max 100**. `azure` has ~626 icons → 7 pages at 100. |
| `GET /search?q={term}` | Same envelope, **fixed 20 results** (`pageSize` ignored) | **Fuzzy — never empty.** A nonsense query still returns 20 unrelated icons. |
| `GET /{category}/{slug}.svg` | The SVG (viewBox `0 0 18 18`) | **GET only — HEAD returns 405.** Verify with GET. |

Item shape: `{name, slug, folder, category, svgUrl}` — `svgUrl` is the full, ready-to-use URL.

## Non-negotiable rules

1. **Resolve, never guess slugs.** Always discover via `/search` (or `/list` paging); a hand-built slug that 404s breaks the rendered diagram silently.
2. **Search is fuzzy and never empty — validate every hit.** Accept a result only if its `name` actually denotes the service you are placing; if the top hits are unrelated, refine the query or page through `/list`. Do NOT take `items[0]` blindly.
3. **Prefer the exact product icon over a generic.** `container-apps-environments` for Container Apps (no plain `container-apps` exists), `function-apps` for Functions, `storage-accounts` for a Storage Account — not `storage-container`/`blob-block` unless depicting that sub-resource specifically.
4. **One slug per service per document.** Pin the chosen slug in a small icon map and reuse it across all diagrams of the artifact — never mix `app-services` and `app-service-plans` for the same node.
5. **Verify before embedding.** `curl -s -o /dev/null -w "%{http_code}" {svgUrl}` must print `200` (GET, not HEAD).
6. **Typst cannot fetch URLs.** Download each SVG next to the `.typ` file first, then `#image(...)`. Mermaid references the URL directly.

## Resolution workflow

```bash
# 1. discover
curl -s "https://assets.byteaid.io/api/icons/search?q=container%20apps" \
  | python -c "import json,sys; [print(i['slug'],'|',i['name']) for i in json.load(sys.stdin)['items']]"
# 2. pick the row whose name matches the service (rule 2+3), note its svgUrl
# 3. verify
curl -s -o /dev/null -w "%{http_code}\n" "https://assets.byteaid.io/api/icons/azure/container-apps-environments.svg"
```

```powershell
(Invoke-RestMethod "https://assets.byteaid.io/api/icons/search?q=container apps").items | Select-Object slug, name
(Invoke-WebRequest "https://assets.byteaid.io/api/icons/azure/container-apps-environments.svg" -Method Get).StatusCode
```

Full catalog sweep (when search misses): iterate `page=1..totalPages` with `pageSize=100` and grep the slugs locally.

## Dispatch

| Need | Read |
|---|---|
| Embedding recipes: mermaid img-in-label, Typst download + `#image` helper, sizing, renderer caveats | [embedding-recipes.md](embedding-recipes.md) |
