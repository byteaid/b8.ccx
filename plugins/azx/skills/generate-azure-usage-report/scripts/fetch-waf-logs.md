# fetch-waf-logs

One-line summary: aggregate Web Application Firewall events (App Gateway + Front Door) from Log Analytics into `waf-logs.json`.

## Purpose

Stage 5. Feeds the **WAF sub-report inside the Seguridad section** — here WAF means the **firewall product**, not the Well-Architected Framework. Aggregation happens **in KQL**, so an attack flood of millions of events still stages a few hundred rows. Three sources are probed independently and only those yielding rows are emitted:

| Source key | Table / filter |
|---|---|
| `application-gateway` | `AzureDiagnostics` where `Category == 'ApplicationGatewayFirewallLog'` (legacy Azure-diagnostics mode) |
| `application-gateway-dedicated` | `AGWFirewallLogs` (resource-specific / dedicated table mode) |
| `front-door-classic` | `AzureDiagnostics` where `Category == 'FrontdoorWebApplicationFirewallLog'` |
| `front-door` | `FrontDoorWebApplicationFirewallLog` (resource-specific table) |

Per source: `byAction` (totals), `topRules` (with one sample message when available), `topClientIps`, `topUris`, `topRuleUris` (rule×URI concentration — input for the false-positive heuristic in `build-usage-report`), `dailyTrend`.

## When to use

- Any run against a subscription with WAF policies or App Gateway/Front Door WAF and diagnostics wired to Log Analytics.

## When NOT to use

- No Log Analytics workspace — omit `--workspace-id`; the stage writes an empty payload, exits 0, and the WAF section degrades to inventory-only.

## Invocation

```bash
dotnet run ${CLAUDE_SKILL_DIR}/scripts/fetch-waf-logs.cs -- \
  --stage-dir ./run-2026-06 \
  --workspace-id 11111111-2222-3333-4444-555555555555 \
  --start 2026-06-01T00:00:00Z \
  --end 2026-07-01T00:00:00Z
```

## Arguments

| Name | Required | Description |
|---|---|---|
| `--stage-dir` | yes | Staging directory. |
| `--workspace-id` | no | Log Analytics workspace GUID. Absent → empty graceful output. |
| `--start` / `--end` | yes | Analysis window (normally the protagonist month). |
| `--top` | no | Rows per top-N aggregate. Default `15`. |
| `--force` | no | Overwrite an existing `waf-logs.json`. |

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Success (including the no-workspace empty case). |
| `1` | Write conflict. |
| `3` | Azure auth failed. |

## Stdout / stderr contract

- stdout: silent.
- stderr: `[fetch-waf-logs]` per-source event totals; per-query failures are isolated (a missing table just skips that source).

## Side effects

- Reads: nothing local.
- Writes: `{stage-dir}/waf-logs.json`.
- Network: Log Analytics (`api.loganalytics.io` via `Azure.Monitor.Query`).
