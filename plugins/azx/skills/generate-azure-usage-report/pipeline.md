# Pipeline Contracts — Azure Usage Report (Light)

**Version:** v2.1.0
**Updated:** 2026-07-21

L2 leaf. Wire-format contracts every stage MUST honour. Per-stage arguments, exit codes, and side effects live in each script's paired `.md` under `scripts/` — this file owns the cross-stage schemas: the staged inputs stage 9 consumes, the `usage-report.json` it emits, the classification thresholds, and the `narrative.json` schema.

## Common conventions

- All scripts take `--stage-dir <path>` (created on demand) and refuse to overwrite an existing output without `--force`.
- Auth: `DefaultAzureCredential` only. Multi-tenant operators bind each invocation with an inline `AZURE_CONFIG_DIR=<profile-dir>` env var.
- Exit codes: `0` success (including graceful degradation); `1` write conflict / generic failure; `2` contract violation (missing/malformed predecessor); `3` Azure auth failure.
- Staged JSON: UTF-8, camelCase, 2-space indent, nulls omitted, atomic write (tmp → rename). Stdout silent on success; stderr carries `[stage-name]` progress lines.
- Dates ISO-8601 UTC (`2026-06-01T00:00:00Z`).

## Two analysis windows (IMPORTANT)

- **Cost window** — the 3 months the report covers. Drives `fetch-costs`. The LAST month is the protagonist (`signals.currentMonth`).
- **Analysis month** — ONE recent full month driving `fetch-metrics` and `fetch-waf-logs`. Azure Monitor metrics retain ~93 days; pointing stage 4 at an older month returns empty series. Normally analysis month == the cost window's last month.

## Staged inputs (stages 1-8)

Shapes stage 9 depends on (each producer's `.md` documents the rest):

- `subscriptions.json` — `{ generatedAt, subscriptions: [{ id, name }] }`.
- `resources.json` — `{ subscriptionId, resources: [{ id, name, type, region, resourceGroup, kind?, sku?: { name, tier, capacity? }, tags?, properties? }] }`. `type` is a flat mixed-case string; `sku` a sibling object; `properties` an ARM passthrough (stage 9 reads `diskState`, `ipConfiguration`, `numberOfSites`, `policySettings`, `webApplicationFirewallConfiguration`, `allowBlobPublicAccess` from it).
- `costs.json` — `{ subscriptionId, start, end, granularity, costs: [{ cloudId, day, value, chargeType }] }`. `cloudId` lowercase ARM id; joins are by `id.ToLowerInvariant()`.
- `metrics.json` — `{ subscriptionId, start, end, metrics: [{ cloudId, name, aggregation, granularity, values: [{ timestamp, value }] }] }`. CPU family = `CpuPercentage` | `cpu_percent` | `Percentage CPU`; other series (e.g. `MemoryPercentage`) are staged but not classified.
- `waf-logs.json` — `{ workspaceId|null, start, end, sources: [{ source, byAction, topRules, topClientIps, topUris, topRuleUris, dailyTrend }] }`; every aggregate row carries `hits` plus its dimension columns.
- `advisor.json` — `{ subscriptionId, recommendations: [{ category, impact, impactedField, impactedValue, problem, solution, resourceId?, annualSavingsAmount?, savingsCurrency? }] }`. Categories: `Cost | Security | HighAvailability | OperationalExcellence | Performance`.
- `security.json` — `{ subscriptionId, present, secureScore?: { current, max, percentage }, assessmentCounts: { total, unhealthy, bySeverity }, unhealthyAssessments: [{ displayName, severity, resourceId? }], sqlAuditing: [{ serverId, name, state }], webAccessRestrictions: [{ siteId, name, publicNetworkAccess?, restrictionCount, denyAll, openToAll }], dbFirewallRules: [{ serverId, name, startIpAddress, endIpAddress }] }`. `percentage` already ×100; `sqlAuditing`, `webAccessRestrictions`, and `dbFirewallRules` enumerate from `resources.json` (SQL servers / sites / SQL+flexible servers); `openToAll` = the site's direct endpoint accepts traffic from anywhere (no PNA-disable, no deny-all, no specific restrictions).
- `resilience.json` — `{ subscriptionId, vaults, protectedItems: [{ sourceResourceId, ... }], webCertificates: [{ name, expirationDate, issuer, hostNames }], autoscaleSettings: [{ targetResourceUri, enabled, minCapacity, maxCapacity }], sqlBackupPolicies: [{ databaseId, retentionDays, weeklyRetention, monthlyRetention, yearlyRetention }], storageBlobServices: [{ accountId, blobSoftDeleteEnabled, blobSoftDeleteDays?, containerSoftDeleteEnabled, versioningEnabled }], resourceZones: [{ id, zones: [], availabilitySetId? }], fileShares: [{ accountId, shareName }], webAppBackups: [{ siteId, status: "Configured"|"NotConfigured"|"NotVerifiable", retentionDays?, frequency? }], keyVaultCertificates: [{ secretId, expirationDate? }] }`. LTR values of `PT0S` mean "not configured"; `resourceZones` covers the types whose `zones` array is top-level (VMs, VMSS, Redis, App Gateway); the SQL/storage/site/Key-Vault sub-fetches enumerate from `resources.json` and stay empty when it is not staged. `webAppBackups` and `keyVaultCertificates` are the privileged checks — Contributor (or higher) + Key Vault data-plane read is ASSUMED and always attempted; denial degrades per-resource to `NotVerifiable` / missing `expirationDate`, never a failed stage. Embedded App Gateway listener certs (`publicCertData`) are still parsed by stage 9 from `resources.json` properties (`sslCertificates` + `httpListeners`).

## Stage 9 — `usage-report.json`

```jsonc
{
  "generatedAt": "...Z",
  "subscriptionId": "...",
  "period": { "start": "...Z", "end": "...Z" },
  "months": ["2026-04", "2026-05", "2026-06"],   // ordered cost-window months
  "analysisMonth": "2026-06",                      // metrics month (falls back to last cost month)
  "currency": "USD",
  "signals": { /* deterministic facts — below */ },
  "sections": [
    {
      "id": "costos",                               // fixed ids, fixed order — see table
      "title": "Costos",
      "blocks": [ { "id", "title", "displayType", "description", "data": {} } ],
      "analysis": { "resumen": "", "observaciones": [], "recomendaciones": [] }   // EMPTY — agent fills via narrative.json
    }
  ]
}
```

### Fixed template (the predictability contract)

Section ids, block ids, and their order are **FIXED — identical on every run**. A block with no data still renders, as a one-line "Sin hallazgos en el período." — blocks NEVER appear/disappear between runs, so consecutive monthly reports are structurally identical and directly comparable. WAF blocks are merged across log sources (AzureDiagnostics vs dedicated tables, App Gateway vs Front Door) so the skeleton never varies with the logging mode. `description` is a short **Spanish reader-facing line stating what the block evaluates** — the renderer prints it under every block title as an intro (Paragraph blocks render it as their body), and it doubles as grounding for the narrative agent.

| Section | Canonical blocks (fixed order, with caps) |
|---|---|
| `costos` | `cost-trend` (MonthlyTrend chart) · `cost-by-service` (GroupedMonthly top 5) · `cost-top-resources` (GroupedMonthly top 5) · `sizing-summary` (BarList, CPU classification) · `utilization` (Table, top 12 Zombie/Sobredimensionado by monthly cost + grouped residual rows, with Entorno) · `unused` (Table, 10 + grouped residual rows) · `advisor-cost` (Table, 8 + grouped residual, with savings) |
| `seguridad` | `security-severity` (BarList; description carries the secure score or its unavailability) · `security-findings` (Table, 10 + per-severity grouped rows) · `security-surface` (CountTable; includes SQL servers without auditing) · `waf-exposure` (Table, findings only, grouped rows: apps whose public traffic bypasses the WAF) · `infra-exposure` (Table, findings only, grouped rows: infra accepting any-origin traffic) · `waf-inventory` (Table) · `waf-actions` (BarList, merged) · `waf-top-rules` (Table, top 8, merged) · `waf-top-ips` (Table, top 8, merged) · `waf-fp-candidates` (Table) · `advisor-security` (Table, 8 + grouped residual) |
| `fiabilidad` | `backup-gaps` (Table, grouped rows, severity-ranked, with Entorno; the description summarizes family coverage — the per-family coverage table was retired, family totals live in `signals.reliability.families`) · `advisor-reliability` (Table, 8 + grouped residual) |
| `disponibilidad` | `certificates` (Table, 12 nearest expiry + per-status grouped rows, App Service + App Gateway listeners) · `redundancy` (Table, grouped rows, findings first: level Local/Zona/Regional per resource) |
| `rendimiento` | `saturated` (Table, with Entorno) · `scaling` (Table, grouped rows, findings first) · `advisor-performance` (Table, 8 + grouped residual) |
| `operacion` | `tagging` (CountTable) · `tag-keys` (Table) · `fragmentation` (CountTable) · `naming` (Table) · `advisor-operational` (Table, 8 + grouped residual) |

**Grouped-row invariant (compaction):** a resource table NEVER emits a standalone `… y N más` row. Resources sharing the same characteristics tuple (every non-name column: type, environment, exposure/config, finding, severity/status) collapse into ONE row whose first cell lists their names inline up to a character budget and then closes with `… y N más` **in the same cell** (`Helpers.NameList`). A different tuple always starts a new row with its own counter. Metric tables (`utilization`, `unused`, `certificates`, advisor blocks) keep their top-N individual rows — the per-resource figures are the point — and apply the same inline grouping to the residual. Visible counts ALWAYS reconcile with `signals` (the WAF top-N aggregates are event rankings, not resource lists, and are exempt); full uncapped detail remains in `signals` (and the staged inputs) for audit.

### displayType vocabulary

| displayType | `data` shape | Rendered as |
|---|---|---|
| `MonthlyTrend` | `{ months, totals: [{ month, value, delta, direction }] }` | **Column chart**: one labeled bar per month (current month emphasized), MoM delta under each column |
| `GroupedMonthly` | `{ months, rows: [{ label, values[] }], total[] }` | Concepto × month columns + bold Total row |
| `CountTable` | `{ rows: [{ label, count }], total }` | Concepto / Cantidad + bold Total |
| `Table` | `{ headers[], align[]?, rows[][], dense? }` | Generic zebra table; `align` per column (`l`/`r`/`c`); status tokens auto-colored |
| `BarList` | `{ format: "count"\|"money", rows: [{ label, value, color? }] }` | **Horizontal bar list**: label · proportional bar · value; `color` ∈ rojo/naranja/verde/azul (semáforo) or absent (primary) |
| `Paragraph` | `{}` (renders the block's `description`) | Plain prose |

### Classification thresholds (decided here, never by the narrative)

| Verdict | Rule (CPU = analysis-month series) |
|---|---|
| `Zombie` | idle share (samples < 10%) ≥ 99% |
| `Sobredimensionado` | idle ≥ 80% OR (avg < 15% AND p95 < 40%) |
| `Saturado` | ≥ 90% share ≥ 5% of samples OR avg ≥ 80% |
| `Correcto` | otherwise |
| Recurso sin uso | disk `diskState == Unattached`; public IP with no `ipConfiguration`/`natGateway`; plan `numberOfSites == 0` |
| FP WAF candidato | a rule×URI combo holds ≥ 80% of the rule's hits with ≥ 50 events |
| Certificado | expiry < 0d `Vencido` / ≤ 30d `Crítico` / ≤ 60d `Advertencia` / else OK; App Gateway `publicCertData` parsed as X509, Key Vault refs resolved via the staged data-plane expiry — `No verificable` ONLY when that access was denied |

### Environment weighting (decided here, never by the narrative)

| Aspect | Rule |
|---|---|
| Entorno | tags `environment`/`env`/`entorno`/`ambiente`/`stage` first, then name/RG tokens (`prod`, `dev`, `qa`, `uat`, `stg`, …), then flat name suffix → `Prod` / `Dev/Test` / `—` |
| Severidad | Prod → full severity · Dev/Test → `Informativo` (a gap there is a deliberate trade-off, never grave) · unclassified → `Advertencia` (conservative) |
| Backup gap | VM / Azure Files share sin Recovery Services → `Crítico` / `Advertencia` (Prod); SQL DB sin LTR → `Advertencia` (Prod); storage sin blob soft delete → `Advertencia` (Prod); App Service (no function app) sin App Service Backup → `Advertencia` (Prod); backup de sitio no verificable (sin Contributor) → `Informativo`; Container Apps → sin estado persistente, nunca gap. Function-App CONTENT shares (nombre de un site + sufijo hex; más las huérfanas con sufijo hex en las mismas cuentas) → familia propia "contenido de apps", nunca gap — solo las shares de datos gestionadas por el usuario generan hallazgos |
| Redundancia | each resource gets a LEVEL — `Local` < `Zona` (zoneRedundant flags, ZRS, zones array, HA ZoneRedundant) < `Regional` (GRS/GZRS, Cosmos multi-región) — and the ask is env-driven: Prod + unclassified require ≥ `Zona`, Dev/Test is satisfied by `Local`. Below the ask → `Advertencia`; Config column carries the evidence (SKU, zonas, HA mode, backup storage) |
| Auditoría SQL | server `auditingSettings/default.state != Enabled` → cuenta en `security-surface` + nombres en `signals.security.sqlServersWithoutAuditing` |
| Cobertura WAF | MANDATORY: toda app Prod publica a través de un gateway con WAF. App detrás del WAF = alguno de sus hostnames/FQDN aparece en un backend pool de un App Gateway con WAF. Endpoint directo abierto = `openToAll` del staged `webAccessRestrictions` (sites) / ingress sin `ipSecurityRestrictions` (Container Apps). Sin WAF + abierto → `Crítico` (Prod); detrás del WAF pero abierto → bypass, `Advertencia`; detrás de gateway sin WAF → `Advertencia`; restringida/privada o ingress interno → OK. Container App cuyo managed environment es interno (`vnetConfiguration.internal`) → sin exposición a internet aunque su ingress sea `external` (el load balancer del environment es privado y el FQDN solo resuelve dentro de la VNet) → OK. Env-weighted como siempre |
| Exposición de infra | LAXER rule: infra que acepta tráfico de CUALQUIER origen → `Advertencia` (Prod). Markers: DB servers con regla de firewall `0.0.0.0-255.255.255.255` (staged `dbFirewallRules`); storage/Key Vault con `networkAcls.defaultAction Allow` (o sin ACLs); Cosmos sin `ipRules` ni VNet filter; Redis con `publicNetworkAccess` habilitado. `publicNetworkAccess Disabled` / ACLs Deny / reglas específicas → OK; reglas no staged → `Exposición no verificada` (Informativo) |
| Escalamiento | plan Standard+ / VMSS sin autoscale setting → env-weighted; Container App sin scale rules y réplicas fijas → env-weighted; SQL DB aprovisionado fijo (ni serverless ni elastic pool) → `Informativo` (siempre) |

### `signals` block (grounds the narrative)

```jsonc
"signals": {
  "currentMonth": "2026-06", "currentMonthCost": 665.67, "momGrowthPct": 2.6,
  "priorMonths": [{ "month", "value" }], "totalCostByMonth": [{ "month", "value" }],
  "topServiceTypes": [{ "label", "cost" }],       // CURRENT-month top 5
  "topResources": [{ "label", "cost" }],          // current-month top 5
  "resourceCounts": { "total" },
  "environments": { "prod", "devTest", "unclassified" },   // resource counts per environment
  "sizing": {
    "zombies|oversized": [{ "label", "type", "sku", "environment", "monthlyCost", ... }],
    "correctCount", "meteredCount",
    "zombieMonthlyCost", "oversizedMonthlyCost",
    "unused": [{ "label", "type", "finding", "monthlyCost" }], "unusedMonthlyCost"
  },
  "waf": { "present", "totalEvents", "blockedEvents", "topRules": [...], "fpCandidateCount" },
  "security": {
    "defenderPresent", "secureScorePct", "unhealthyBySeverity": { "high", "medium", "low" },
    "sqlServersEvaluated", "sqlServersWithoutAuditing": [names],
    "wafExposure": { "directCount", "bypassCount", "nonWafGatewayCount", "okCount", "findings": [{ "label", "type", "environment", "exposure", "finding", "severity" }] },
    "infraExposure": { "openCount", "okCount", "findings": [{ "label", "type", "environment", "exposure", "finding", "severity" }] },
    "advisorSecurityCount"
  },
  "reliability": {
    "families": [{ "family", "total", "protectedCount", "unprotected": [names] }],   // VMs, SQL, Cosmos, flexible, Storage, Azure Files, Container Apps, App Services
    "gaps": [{ "label", "type", "environment", "finding", "severity" }],
    "criticalGapCount", "advisorReliabilityCount"
  },
  "availability": {
    "certsExpired", "certsCritical", "certsWarning", "certsUnverifiable",
    "redundancy": {
      "localCount", "zonaCount", "regionalCount", "okCount",
      "warnings": [{ "label", "type", "environment", "level", "config" }]
    }
  },
  "performance": {
    "saturated": [{ "label", "type", "sku", "environment", "p95Cpu", "monthlyCost" }],
    "scalingFindings": [{ "label", "type", "environment", "finding", "severity" }],
    "scalingOkCount", "advisorPerformanceCount"
  },
  "operations": { "tagCoveragePct", "untaggedCount", "inconsistentTagKeyCount", "resourceGroups", "regions", "namingPatterns": [{ "pattern", "count" }] },
  "advisor": { "total", "byCategory": {...}, "estAnnualSavingsUsd" }
}
```

## Narrative step — `narrative.json` (agent-authored)

Authored with the FROZEN prompt in [narrative-stage.md](narrative-stage.md), verbatim. Schema (keys map 1:1 to section ids):

```jsonc
{
  "executiveSummary": {
    "estadoGeneral": "…",                        // current-month state, MoM as context
    "atencion": "…",                              // single most important finding; "" if none
    "hallazgosClave": [{ "titulo", "texto" }],    // 3-4
    "riesgos": [{ "riesgo", "impacto", "probabilidad": "Alta|Media|Baja", "accion" }],   // 3
    "proximosPasos": { "inmediato": [], "cortoPlazo": [], "medianoPlazo": [] }
  },
  "sections": {
    "<sectionId>": { "resumen": "…", "observaciones": ["…"], "recomendaciones": ["…"] }
  }
}
```

There is deliberately NO separate conclusion — the light report closes with the executive summary's próximos pasos. `render-typst` tolerates a missing `narrative.json` (data tables only) and skips any empty part.

## Stage 10 — render

Reads `usage-report.json` + optional `narrative.json` (`--narrative`, default `{stage-dir}/narrative.json`), writes `typst/` (self-contained `main.typ`), optionally compiles `output.pdf`. Layout and theming in [typst-output.md](typst-output.md).

## Error semantics

| Failure | Behaviour |
|---|---|
| Stage called, hard predecessor missing (`resources.json` for 9, `usage-report.json` for 10) | Exit 2, stderr names the file. |
| Optional input missing (stages 5-8 outputs) | Stage 9 proceeds; the affected section thins. |
| Azure auth fails | Exit 3, stderr says how to `az login`. |
| 429 (ARM / Monitor / Log Analytics) | Bounded retry (10/20/40s); metrics series that exhaust retries are skipped (re-run backfills). |
| Output exists, no `--force` | Exit 1. |
| `--compile` without `typst` on PATH | `typst/` IS written; exit 1 with the manual command on stderr. |

## Deliverable naming

`{client}-usage-report-{mmm}-{yyyy}.pdf` — `{mmm}` lowercase three-letter English month of **`analysisMonth`** (the protagonist month), e.g. `acme-usage-report-jun-2026.pdf`. Keep the full staging dir as audit evidence next to the deliverable.
