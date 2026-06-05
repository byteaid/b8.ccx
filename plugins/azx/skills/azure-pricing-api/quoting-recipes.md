# Quoting Recipes — From Meters to an Accurate Quote

**Version:** v1.0.0
**Updated:** 2026-06-04

L2 leaf. The workflow that turns Retail Prices API responses into an itemized, dated, defensible cost estimate. Field semantics live in [api-reference.md](api-reference.md).

## Quote workflow

1. **Inventory the architecture.** One line per billable component: service, SKU/tier, region, expected quantity (instances, GB, requests, vCPU-s).
2. **Discover the exact meter names.** For each component, run a discovery query (`serviceName` + `armRegionName` + `priceType eq 'Consumption'`), list distinct `meterName`/`productName`/`skuName`, and pick the rows that match the component. Do NOT guess meter names — discover them.
3. **Capture all relevant rates per meter:** the `Consumption` row, the `Reservation` rows (1y/3y — remember: term-total price, divide by term hours for an effective hourly), and the `savingsPlan` array (already hourly).
4. **Normalize units.** Read `unitOfMeasure` before multiplying: `1 Hour` × 730 h/month; `1 GB/Month` × stored GB; `10K` ops × monthly ops ÷ 10,000; `1M` requests likewise. Never assume hourly.
5. **Compute monthly per row** = unit price × normalized monthly quantity. Sum into the architecture total.
6. **Present** the quote table — columns: component, meter (`meterName` + `meterId`), region, unit price, unit, est. monthly qty, monthly cost — with the retrieval date and currency in the header, assumptions listed under it, and commitment alternatives (RI / savings plan) as clearly labeled option rows, never blended into the base total.

## Standard constants

| Constant | Value | Use |
|---|---|---|
| Hours/month | **730** | Monthly estimate from hourly meters (Azure calculator convention). |
| 1-year RI hours | 8,760 | `reservationTerm` total ÷ 8,760 = effective hourly. |
| 3-year RI hours | 26,280 | Same for 3 years. |

## Recipes

### Discover the exact serviceName / meters for a service

```bash
curl -sG "https://prices.azure.com/api/retail/prices" \
  --data-urlencode "api-version=2023-01-01-preview" \
  --data-urlencode "\$filter=serviceFamily eq 'Containers' and armRegionName eq 'eastus2'" \
  | jq '[.Items[].serviceName] | unique'
# then drill into the chosen serviceName and list its meters:
curl -sG "https://prices.azure.com/api/retail/prices" \
  --data-urlencode "api-version=2023-01-01-preview" \
  --data-urlencode "\$filter=serviceName eq 'Azure Container Apps' and armRegionName eq 'eastus2' and priceType eq 'Consumption'" \
  | jq -r '.Items[] | [.meterName, .retailPrice, .unitOfMeasure, .productName] | @tsv'
```

### Price a VM SKU with all commitment options

```bash
curl -sG "https://prices.azure.com/api/retail/prices" \
  --data-urlencode "api-version=2023-01-01-preview" \
  --data-urlencode "\$filter=armSkuName eq 'Standard_D4s_v5' and armRegionName eq 'eastus2'" \
  | jq '.Items[] | {meterName, productName, priceType, retailPrice, reservationTerm, unitOfMeasure, savingsPlan}'
```

Expect per region: Linux Consumption, Windows Consumption (`productName` ends in `Windows`), Spot/Low-Priority meters, 1y/3y `Reservation` rows, and `savingsPlan` entries on the Consumption rows.

### Region price comparison for one SKU

```bash
for r in eastus2 westeurope southcentralus brazilsouth; do
  curl -sG "https://prices.azure.com/api/retail/prices" \
    --data-urlencode "api-version=2023-01-01-preview" \
    --data-urlencode "\$filter=armSkuName eq 'Standard_D4s_v5' and armRegionName eq '$r' and priceType eq 'Consumption'" \
    | jq -r --arg r "$r" '.Items[] | select(.productName | test("Windows") | not) | select(.meterName | test("Spot|Low Priority") | not) | [$r, .retailPrice] | @tsv'
done
```

## Pitfalls (each one has produced a wrong quote)

| Pitfall | Symptom | Fix |
|---|---|---|
| Spot / Low Priority meters mixed in | Quote 60–90% too cheap | Exclude `meterName` containing `Spot` / `Low Priority` unless quoting Spot deliberately. |
| Windows license rows | Two prices for the "same" SKU | `productName` suffix `Windows` = compute + license. Pick by the OS actually planned. |
| `DevTestConsumption` rows | Slightly cheaper rates leak in | Filter `priceType eq 'Consumption'` explicitly. |
| Reservation price read as hourly | Quote ~8,760× too expensive | `Reservation` `retailPrice` is the FULL term price. Divide by term hours. |
| Page 1 only | Missing meters in broad queries | Always follow `NextPageLink`. |
| Casing miss in filter | Empty `Items`, assumed "free"/"unavailable" | Values are case sensitive on the preview version; verify against discovery output. |
| Tiered meters (multiple `tierMinimumUnits`) | Wrong unit price for the volume | Pick the row whose tier bracket contains the expected monthly volume; for graduated tiers, sum per bracket. |
| Non-primary meters double-count | Duplicate rows per meter | Prefer `isPrimaryMeterRegion == true` or pass `meterRegion='primary'`. |
| Unit mismatch | Off by 730× / 10,000× | Multiply only after normalizing `unitOfMeasure`. |
| Free grants ignored or over-trusted | ACA/Functions/etc. monthly free grants | The API returns the paid rate only; free grants are NOT in the API. Note them as an assumption from service docs, never invent amounts. |
| Stale prices reused | Quote drifts from reality | Re-query in the session that emits the quote; print the retrieval date. |

## Quote table template

```markdown
### Cost estimate — {architecture} ({CUR}, priced {YYYY-MM-DD}, region {armRegionName})

| Component | Meter | Unit price | Unit | Est. monthly qty | Monthly |
|---|---|---|---|---|---|
| {component} | {meterName} (`{meterId}`) | {retailPrice} | {unitOfMeasure} | {qty} | {cost} |
| **Total** | | | | | **{sum}** |

**Commitment options:** {SKU} 1y savings plan {rate}/h (−{pct}%), 3y RI effective {rate}/h (−{pct}%).
**Assumptions:** 730 h/month; {workload assumptions}; free grants not netted out.
```
