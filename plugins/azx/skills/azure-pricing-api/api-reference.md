# Azure Retail Prices API — Reference

**Version:** v1.0.0
**Updated:** 2026-06-04

L2 leaf. Full request/response contract. Upstream: <https://learn.microsoft.com/en-us/rest/api/cost-management/retail-prices/azure-retail-prices>.

## Endpoint

```
GET https://prices.azure.com/api/retail/prices
```

No authentication, no subscription, no API key. Commercial-cloud prices only (no sovereign clouds via this endpoint).

## Query parameters

| Param | Values | Notes |
|---|---|---|
| `api-version` | `2023-01-01-preview` (use this), `2023-01-01`, `2021-10-01` | Preview = full meter set + `savingsPlan` rates + case-sensitive filters. Omitting it also returns the full meter set but without savings plans. |
| `currencyCode` | `'USD'` (default), `'EUR'`, `'GBP'`, `'JPY'`, `'AUD'`, `'BRL'`, `'CAD'`, `'CHF'`, `'CNY'`, `'DKK'`, `'INR'`, `'KRW'`, `'NOK'`, `'NZD'`, `'RUB'`, `'SEK'`, `'TWD'` … | Quoted with single quotes. Non-USD = reference conversion, not a Microsoft retail price. |
| `meterRegion` | `'primary'` | Restrict to primary meters (the ones billing actually uses). Supported on `2021-10-01`+. Plain query param — NOT inside `$filter`. |
| `$filter` | OData expression | See below. |
| `$skip` | integer | Set automatically by `NextPageLink`; do not hand-roll. |

## `$filter`

Filterable fields: `armRegionName`, `location`, `meterId`, `meterName`, `productId`, `skuId`, `productName`, `skuName`, `serviceName`, `serviceId`, `serviceFamily`, `priceType`, `armSkuName`.

Operators: `eq`, `and`, plus string functions `contains(field, 'x')`, `startswith`, `endswith`. `or` works but is slow on broad fields — prefer two calls. Values single-quoted; **case sensitive** on `2023-01-01-preview`.

```text
$filter=serviceName eq 'Virtual Machines' and armRegionName eq 'eastus2' and priceType eq 'Consumption'
$filter=armSkuName eq 'Standard_D4s_v5' and priceType eq 'Reservation'
$filter=serviceFamily eq 'Compute' and contains(meterName, 'Spot')
$filter=priceType eq 'Reservation'
```

`priceType` values: `Consumption` (pay-as-you-go), `Reservation` (1/3-year RIs — price returned is the FULL upfront term price, not hourly), `DevTestConsumption` (Dev/Test subscription rates — exclude from production quotes).

## Response shape

```json
{
  "BillingCurrency": "USD",
  "CustomerEntityId": "Default",
  "CustomerEntityType": "Retail",
  "Items": [ { ...price record... } ],
  "NextPageLink": "https://prices.azure.com:443/api/retail/prices?$filter=...&$skip=1000",
  "Count": 1000
}
```

Max 1,000 items per page; follow `NextPageLink` until `null`.

### Price record fields

| Field | Example | Meaning |
|---|---|---|
| `currencyCode` | `USD` | Currency of `retailPrice`/`unitPrice`. |
| `retailPrice` | `0.176346` | Microsoft retail price, no discount. For `Reservation` rows: total price for the whole `reservationTerm`. |
| `unitPrice` | `0.176346` | Same as `retailPrice` at retail. |
| `tierMinimumUnits` | `0.0` | Tiered pricing: minimum consumption for this row's price. Multiple rows per meter = graduated tiers; pick by expected volume. |
| `unitOfMeasure` | `1 Hour`, `1 GB/Month`, `10K` | What one unit means. Normalize before multiplying. |
| `priceType` / `type` | `Consumption` | `Consumption` / `Reservation` / `DevTestConsumption`. |
| `reservationTerm` | `1 Year`, `3 Years` | Only on `Reservation` rows. |
| `savingsPlan` | `[{unitPrice, retailPrice, term}]` | Only with `api-version=2023-01-01-preview`, only on eligible meters. Hourly rate per `term` (`1 Year` / `3 Years`). |
| `armRegionName` | `southcentralus` | ARM region — the field to filter on. |
| `location` | `US South Central` | Display name of the region. |
| `armSkuName` | `Standard_D4s_v5` | SKU name as registered in ARM — matches what IaC uses. |
| `meterId` | GUID | Unique meter identity — cite it in quotes for traceability. |
| `meterName` | `D4s v5` | Human meter name. `Spot` / `Low Priority` variants live here. |
| `productId` / `productName` | `DZH318Z0BQPS` / `Virtual Machines Dsv5 Series Windows` | Product. **OS/license is encoded in `productName`** (`… Windows` suffix), not in `armSkuName`. |
| `skuId` / `skuName` | `DZH318Z0BQPS/00TG` / `D4s v5` | SKU within the product. |
| `serviceName` / `serviceId` | `Virtual Machines` | Service. |
| `serviceFamily` | `Compute` | Top-level family (list below). |
| `isPrimaryMeterRegion` | `true` | Primary meters are the ones used for billing. Prefer `true` rows. |
| `effectiveStartDate` | `2020-08-01T00:00:00Z` | When the price became effective. |

## `serviceFamily` values

`Analytics`, `Azure Arc`, `Azure Communication Services`, `Azure Security`, `Azure Stack`, `Compute`, `Containers`, `Data`, `Databases`, `Developer Tools`, `Dynamics`, `Gaming`, `Integration`, `Internet of Things`, `Management and Governance`, `Microsoft Syntex`, `Mixed Reality`, `Networking`, `Other`, `Power Platform`, `Quantum Computing`, `Security`, `Storage`, `Telecommunications`, `Web`, `Windows Virtual Desktop`. (Subject to change upstream.)

## Pagination loop

```bash
url="https://prices.azure.com/api/retail/prices?api-version=2023-01-01-preview&\$filter=$(python3 -c "import urllib.parse,sys;print(urllib.parse.quote(sys.argv[1]))" "serviceName eq 'Virtual Machines' and armRegionName eq 'eastus2' and priceType eq 'Consumption'")"
while [ -n "$url" ] && [ "$url" != "null" ]; do
  page=$(curl -s "$url")
  echo "$page" | jq -r '.Items[] | [.armSkuName, .retailPrice, .unitOfMeasure, .meterName, .productName] | @tsv'
  url=$(echo "$page" | jq -r '.NextPageLink')
done
```

```powershell
$filter = "serviceName eq 'Virtual Machines' and armRegionName eq 'eastus2' and priceType eq 'Consumption'"
$url = "https://prices.azure.com/api/retail/prices?api-version=2023-01-01-preview&`$filter=" + [uri]::EscapeDataString($filter)
$items = @()
while ($url) { $r = Invoke-RestMethod $url; $items += $r.Items; $url = $r.NextPageLink }
$items | Select-Object armSkuName, retailPrice, unitOfMeasure, meterName, productName
```

## Caveats

- Unknown/misspelled filter values return an empty `Items` (HTTP 200), never a 404 — verify casing first when results are empty.
- `serviceName` values are exact display names (`'Azure Container Apps'`, `'Azure App Service'`, `'Storage'`, `'Azure Cosmos DB'`). When unsure, discover with a broad `serviceFamily` query and `jq '[.Items[].serviceName] | unique'`.
- Global services (Front Door, CDN, Traffic Manager, DNS) do NOT use normal regions: their rows carry `armRegionName` of `''` (empty), `'Zone 1'`…`'Zone 7'` (billing zones), or Gov zones. A region-filtered query silently misses them — query by `serviceName`/`productName` without `armRegionName`, paginate fully, then pick the zone covering the traffic origin (Zone 1 = NA/EU).
- Prices change; the API has no historical endpoint. `effectiveStartDate` tells you when the current price started, not what it was before.
- No SLA on the endpoint; it is throttled. Batch with selective filters instead of scraping the whole catalog (~hundreds of thousands of meters).
