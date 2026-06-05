---
name: azure-pricing-api
description: Azure Retail Prices API reference for building accurate, dated Azure cost quotes. Unauthenticated public endpoint `https://prices.azure.com/api/retail/prices` — query retail prices for every Azure service by serviceName / armSkuName / armRegionName / priceType, paginate NextPageLink, capture Consumption vs Reservation vs savings-plan rates, convert meters into itemized monthly estimates. Upstream truth — https://learn.microsoft.com/en-us/rest/api/cost-management/retail-prices/azure-retail-prices.
when_to_use: |
  - Quoting or estimating the cost of an Azure architecture, service, SKU, or region.
  - Comparing prices: SKU vs SKU, region vs region, Consumption vs Reservation vs savings plan, Linux vs Windows, Spot vs regular.
  - Any task mentioning prices.azure.com, Retail Prices API, retailPrice, meterId, armSkuName, priceType, currencyCode, NextPageLink.
  - Verifying or refreshing dated cost figures in an architecture/solution document.
allowed-tools: Bash, PowerShell, Read, WebFetch
user-invocable: false
---

# Azure Retail Prices API

L1 index. Public, unauthenticated, GET-only price catalog for all Azure services. Use it whenever a cost figure is needed — never quote a remembered price.

## Non-negotiable rules

1. **Always pin `api-version=2023-01-01-preview`.** It is backward compatible, returns the full meter set, and is the ONLY version that returns `savingsPlan` rates.
2. **Filter values are case sensitive** in `2023-01-01-preview`: `serviceName eq 'Virtual Machines'` works; `'virtual machines'` returns nothing. An empty `Items` array usually means a casing/spelling miss, not a missing price.
3. **Always paginate.** Max 1,000 records per response; follow `NextPageLink` until `null`. A quote built from page 1 only is wrong.
4. **USD is canonical.** Microsoft prices in USD; any other `currencyCode` is a reference conversion — label it as such in quotes.
5. **Filter by `priceType` explicitly.** Without it you mix `Consumption`, `Reservation`, and `DevTestConsumption` rows and double-count. Default quotes on `Consumption`.
6. **Date every retrieved price.** A price without its retrieval date is unusable in a quote.
7. **Prefer `armSkuName` + `armRegionName` + `priceType`** as the filter triple — it is the most selective and stable combination. Add `meterRegion='primary'` (query param, not `$filter`) to drop non-primary meter noise.

## Canonical call

```bash
curl -sG "https://prices.azure.com/api/retail/prices" \
  --data-urlencode "api-version=2023-01-01-preview" \
  --data-urlencode "currencyCode='USD'" \
  --data-urlencode "\$filter=serviceName eq 'Azure Container Apps' and armRegionName eq 'southcentralus' and priceType eq 'Consumption'"
```

```powershell
$uri = "https://prices.azure.com/api/retail/prices?api-version=2023-01-01-preview&currencyCode='USD'" +
       "&`$filter=" + [uri]::EscapeDataString("serviceName eq 'Azure Container Apps' and armRegionName eq 'southcentralus' and priceType eq 'Consumption'")
(Invoke-RestMethod $uri).Items
```

## Dispatch

| Need | Read |
|---|---|
| Full parameter/filter/response-field reference, serviceFamily list, pagination contract | [api-reference.md](api-reference.md) |
| Quote workflow: discover meters → price Consumption/Reservation/savings plan → compute monthly → present; pitfalls (Spot, Windows-in-productName, tiers, dual meters) | [quoting-recipes.md](quoting-recipes.md) |
