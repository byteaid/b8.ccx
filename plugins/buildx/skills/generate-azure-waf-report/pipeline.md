# Pipeline Contracts — Stage-by-Stage

Every stage is a single `.cs` file-based app under `scripts/`. Stages communicate exclusively through JSON files in a flat staging directory passed via `--stage-dir <path>`. Schemas below are the **wire format** every script MUST honour.

## Common conventions

- All scripts accept `--stage-dir <path>` (required, except `list-subscriptions` where it is optional). The directory is created on demand.
- All scripts authenticate with `DefaultAzureCredential` (no inline credentials, no client secrets).
- All scripts emit structured logs to **stderr** (one line per event, prefixed with the stage name). Stdout is reserved for the JSON artifact when the operator asks for piping (`--output -`); when `--stage-dir` is used, stdout is silent on success.
- All scripts return exit code `0` on success, `1` on a generic failure with stderr explanation, `2` on a contract violation (input JSON missing/malformed), `3` on Azure auth failure.
- All staged JSON is **UTF-8, camelCase, indented 2 spaces, sorted keys**, written atomically (tmp → rename).
- Date ranges use ISO-8601 with explicit timezone (`2026-01-01T00:00:00Z`). Stages that do not need dates ignore them.
- Empty arrays are written explicitly (`"resources": []`); never `null`. Missing keys are a contract violation.

## Staging directory layout

```
{stage-dir}/
  manifest.json              # written by every stage on completion
  subscriptions.json         # stage 1
  resources.json             # stage 2
  costs.json                 # stage 3
  metrics.json               # stage 4
  diagnostic-logs.json       # stage 5
  metric-rules.json          # input to stage 4 (operator-supplied or default)
  report-sets.json           # stage 6
  typst/
    main.typ
    data.json
    cover.typ
    executive-summary.typ
    analysis-section.typ
    conclusion.typ
    theme.typ
    *.typ                    # one per analysis section
  output.pdf                 # optional, stage 7 only if `typst` CLI is on PATH
```

`manifest.json` is append-only history of which stages ran, when, with which arguments and the SHA-256 of the output file. Stages refuse to overwrite without `--force` and refer to the manifest for the previous run.

## Stage 1 — list-subscriptions → `subscriptions.json`

```json
{
  "generatedAt": "2026-04-28T14:00:00Z",
  "subscriptions": [
    { "id": "00000000-0000-0000-0000-000000000000", "name": "Prod" }
  ]
}
```

- Source: `ArmClient.GetSubscriptions().GetAllAsync()`.
- Filters: `--filter <regex>` against `name` (optional).

## Stage 2 — discover-resources → `resources.json`

```json
{
  "subscriptionId": "00000000-...",
  "generatedAt": "2026-04-28T14:00:00Z",
  "detailedDiscovery": false,
  "resources": [
    {
      "cloudId": "/subscriptions/.../resourceGroups/rg/providers/...",
      "name": "myapp-prod",
      "region": "eastus",
      "kind": "app",
      "type": {
        "key": "Microsoft.Web/sites",
        "displayName": "App Service",
        "sku": { "name": "P1v3", "tier": "PremiumV3", "capacity": 2 }
      },
      "tags": { "env": "prod" },
      "properties": { "...": "passthrough from ARM" }
    }
  ]
}
```

- Source: `ArmClient.GetSubscriptionResource(...).GetGenericResourcesAsync()`.
- `--detailed` triggers a per-type enrichment pass (Application Gateway, NSG, VM, App Service, etc.) — equivalent to `EnrichResourceWithDetailedInformation` in the upstream adapter. Detailed mode is required for Reliability and Security pillars.
- `--filter-system-resources` (default `true`) drops master DBs, system topics, etc. — same rules as upstream `IsSystemResource`.

## Stage 3 — fetch-costs → `costs.json`

```json
{
  "subscriptionId": "00000000-...",
  "start": "2026-01-01T00:00:00Z",
  "end": "2026-04-28T00:00:00Z",
  "granularity": "Daily",
  "costs": [
    {
      "cloudId": "/subscriptions/.../resourceGroups/rg/providers/.../foo",
      "day": "2026-01-15",
      "value": 12.34,
      "chargeType": "Usage"
    }
  ]
}
```

- Source: `ArmClient.UsageQueryAsync` with `QueryDataset` grouped by `ResourceId` + `ChargeType`.
- Implements monthly chunking + manual `nextLink` pagination + 429 retry, identical to `AzureCloudAdapter.GetAllCloudCosts`.
- Inputs: `--start`, `--end`, `--granularity {Daily|Monthly}` (default `Daily`).

## Stage 4 — fetch-metrics → `metrics.json`

Reads `metric-rules.json` if present in `--stage-dir`, otherwise loads the operator-supplied path via `--rules <path>`. Default rules ship as an embedded JSON in the script. A rule:

```json
{
  "rules": [
    {
      "cloudType": "microsoft.web/serverfarms",
      "metricName": "CpuPercentage",
      "granularity": "01:00:00",
      "aggregation": "Average"
    }
  ]
}
```

Output:

```json
{
  "subscriptionId": "00000000-...",
  "start": "2026-01-01T00:00:00Z",
  "end": "2026-04-28T00:00:00Z",
  "metrics": [
    {
      "cloudId": "/subscriptions/...",
      "name": "CpuPercentage",
      "aggregation": "Average",
      "granularity": "01:00:00",
      "values": [
        { "timestamp": "2026-01-01T00:00:00Z", "value": 23.5 }
      ]
    }
  ]
}
```

- Source: `MetricsQueryClient.QueryResourceAsync` for each (resource, rule) pair where the rule's `cloudType` matches the resource's type. Mirrors the per-rule loop in `DataAcquisitionService.AcquireData`.
- `--max-parallel <n>` (default 4) bounds concurrent ARM calls.

## Stage 5 — fetch-diagnostic-logs → `diagnostic-logs.json`

Optional. Skipped (with a stderr warning) when `--workspace-id` is absent.

```json
{
  "subscriptionId": "00000000-...",
  "workspaceId": "11111111-...",
  "start": "2026-01-01T00:00:00Z",
  "end": "2026-04-28T00:00:00Z",
  "categories": ["AzureActivity", "ApplicationGatewayFirewallLog"],
  "logs": [
    {
      "cloudId": "/subscriptions/...",
      "timestamp": "2026-04-12T08:01:00Z",
      "category": "AzureActivity",
      "level": "Warning",
      "message": "..."
    }
  ]
}
```

- Source: `LogsQueryClient.QueryWorkspaceAsync` with a templated KQL per category. WAF-pillar relevant defaults: `AzureActivity`, `AppServiceHTTPLogs`, `AppServiceConsoleLogs`, `ApplicationGatewayAccessLog`, `ApplicationGatewayFirewallLog`.
- The script may infer resource IDs from `resources.json` instead of receiving them explicitly — same as `InferWafEnabledResources` in upstream.

## Stage 6 — build-report-sets → `report-sets.json`

Pure-CPU stage. No Azure calls. Reads every preceding JSON, applies a fixed list of processing rules (one per WAF pillar + a generic Inventory section) and emits report sets ready for the renderer.

```json
{
  "generatedAt": "2026-04-28T14:00:00Z",
  "subscriptionId": "00000000-...",
  "period": { "start": "2026-01-01T00:00:00Z", "end": "2026-04-28T00:00:00Z" },
  "pillars": [
    {
      "id": "cost-optimization",
      "name": "Cost Optimization",
      "reportSets": [
        {
          "id": "cost-trend-monthly",
          "title": "Monthly Cost Trend",
          "displayType": "LineChart",
          "description": "Total subscription cost per month over the analysis window.",
          "series": [
            { "name": "Total", "points": [ { "label": "2026-01", "value": 1234.5 } ] }
          ]
        }
      ]
    }
  ]
}
```

- The pillar set follows the Microsoft WAF (Reliability, Security, Cost Optimization, Operational Excellence, Performance Efficiency, Sustainability). v0.1.0 ships only the report sets that the data layer supports out of the box (heavy emphasis on Cost Optimization and Performance Efficiency, which the upstream `Default Azure Analysis` template covers); other pillars are wired with placeholder report sets that surface honestly in the rendered Typst as "no data" until later versions extend the data layer.
- Each `ReportSet` shape mirrors upstream `Display.ReportSet` minus the `Context` dictionary.

## Stage 7 — render-typst → `typst/`, optional `output.pdf`

Reads `report-sets.json` and the operator-supplied branding/meta options:

```
--title "Azure WAF Assessment — Contoso Production"
--company "Contoso"
--logo path/to/logo.png
--primary-color "#1b6ec2"
--secondary-color "#6c757d"
--accent-color "#0dcaf0"
--compile           # call `typst` if found on PATH
```

- Writes a complete Typst project under `{stage-dir}/typst/`:
  - `main.typ` — page setup, header/footer, calls into the section files.
  - `theme.typ` — colors, font choices.
  - `cover.typ`, `executive-summary.typ`, `analysis-section.typ`, `conclusion.typ`.
  - `data.json` — single source of structured data the templates read.
  - `pillar-{id}.typ` — one file per WAF pillar.
- When `--compile` is set and `typst` is on PATH, runs `typst compile main.typ output.pdf` from the typst dir and copies the result up to `{stage-dir}/output.pdf`. Otherwise, prints to stderr the exact command the operator should run.
- See [typst-output.md](typst-output.md) for the template architecture and `data.json` shape.

## Error semantics across stages

| Failure | Behaviour |
|---|---|
| Stage N called but predecessor JSON missing | Exit 2, stderr names the missing file. |
| Predecessor JSON present but malformed | Exit 2, stderr cites the offending key path. |
| Azure auth fails (`DefaultAzureCredential` returns no token) | Exit 3, stderr says how to `az login`. |
| Azure call returns transient 429/5xx | Internal retry with exponential backoff (`AnalysisService.ExecuteWithRetryAsync` shape). |
| Output file already exists and `--force` not set | Exit 1, stderr says "use --force to overwrite". |
| `typst` CLI not on PATH and `--compile` set | Stage succeeds with exit 0, stderr warns and prints the manual command. |

## Resumability rules

A stage's idempotency boundary is `(stage-dir, primary inputs)`. Re-running stage N with the same `--stage-dir` and the same arguments MUST be a no-op when the output file already exists, unless `--force` is set. The manifest records arguments + content hash so the next stage can detect when its inputs changed and warn the operator.

## WAF pillar mapping (informational)

The build-report-sets stage groups its outputs under WAF pillars. Initial coverage:

| Pillar | Fueled by | Coverage in v0.1.0 |
|---|---|---|
| Reliability | resources (detailed) | partial — SKU tier, redundancy hints |
| Security | resources (detailed), diagnostic-logs | partial — public IPs, NSG flag, WAF-on-AppGW count |
| Cost Optimization | costs | full — trend, top resources, regional split |
| Operational Excellence | resources, diagnostic-logs | partial — tag coverage, log-config counts |
| Performance Efficiency | metrics | full — CPU distribution per service tier |
| Sustainability | costs (region carbon proxy), resources | placeholder |

Coverage will grow with later versions; v0.1.0 must produce a coherent report even when a pillar has only the placeholder.
