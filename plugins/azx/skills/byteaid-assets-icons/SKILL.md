---
name: byteaid-assets-icons
description: ByteAid Assets icons API (`https://assets.byteaid.io/api/icons/*`) — SVG service icons for architecture diagrams. Covers the discovery endpoints (categories / list / search), the icon-resolution workflow that maps an Azure service to its CORRECT icon slug, URL verification, and downloading the SVG to a local file. Embedding a resolved icon into an actual diagram (mermaid / Typst) is `azure-diagrams`, not this skill. Currently exposes the `azure` category (~626 icons); the category model anticipates more.
when_to_use: |
  - Resolving which icon (slug + svgUrl) represents a given Azure service, or verifying an icon URL returns 200.
  - Downloading service-icon SVGs to local files for offline consumers (e.g. Typst).
  - Any task mentioning ByteAid Assets, assets.byteaid.io, diagram icons, Azure service icons, svgUrl, icon slug.
  - Invoked by `azure-diagrams` (and transitively `generate-azure-solution-proposal`) to resolve every Azure node's icon BEFORE the diagram is authored.
allowed-tools: Bash, PowerShell, Read, Write, WebFetch
user-invocable: false
---

# ByteAid Assets — Service Icons

L1 index. Unauthenticated GET-only API serving SVG service icons. Base: `https://assets.byteaid.io/api/icons`. This skill RESOLVES + VERIFIES + DOWNLOADS icons; composing them into a diagram is `azure-diagrams`.

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
5. **Verify before handing off.** `curl -s -o /dev/null -w "%{http_code}" {svgUrl}` must print `200` (GET, not HEAD). A consumer (`azure-diagrams`) trusts that every slug it receives was verified here.
6. **Download with `curl -sf`** (fail on HTTP error) so a bad slug aborts loudly instead of writing an HTML error page into the `.svg`. Offline consumers (Typst) need the file on disk; embedding it into a diagram is `azure-diagrams`.

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

## Download

Offline consumers (Typst) need the SVG on disk. Use `curl -sf` so a bad slug aborts loudly. The destination folder is the consumer's choice — a host procedure may mandate its own layout (e.g. `generate-azure-solution-proposal` requires `.assets/icons/`).

```bash
mkdir -p icons
for slug in container-apps-environments azure-sql storage-accounts; do
  curl -sf -o "icons/$slug.svg" "https://assets.byteaid.io/api/icons/azure/$slug.svg" || echo "MISS: $slug" >&2
done
```

```powershell
New-Item -ItemType Directory -Force icons | Out-Null
'container-apps-environments','azure-sql','storage-accounts' | ForEach-Object {
  Invoke-WebRequest "https://assets.byteaid.io/api/icons/azure/$_.svg" -OutFile "icons/$_.svg"
}
```

## Cross-references

- `azure-diagrams` — composing the resolved/downloaded icons into a mermaid or Typst architecture diagram (img-in-label, `#image` helper, sizing, renderer caveats). Resolve + verify + download HERE first, then author the diagram there.
