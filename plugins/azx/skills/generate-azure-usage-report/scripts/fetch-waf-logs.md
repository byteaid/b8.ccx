# fetch-waf-logs

One-line summary: aggregate Web Application Firewall activity (App Gateway + Front Door) from Log Analytics into `waf-logs.json` — in two units, events and requests.

## Purpose

Stage 5. Feeds the **WAF sub-report inside the Seguridad section** — here WAF means the **firewall product**, not the Well-Architected Framework. Aggregation happens **in KQL**, so an attack flood of millions of events still stages a few hundred rows. Four sources are probed independently and only those yielding rows are emitted:

| Source key | Table / filter |
|---|---|
| `application-gateway` | `AzureDiagnostics` where `Category == 'ApplicationGatewayFirewallLog'` (legacy Azure-diagnostics mode) |
| `application-gateway-dedicated` | `AGWFirewallLogs` (resource-specific / dedicated table mode) |
| `front-door-classic` | `AzureDiagnostics` where `Category == 'FrontdoorWebApplicationFirewallLog'` |
| `front-door` | `FrontDoorWebApplicationFirewallLog` (resource-specific table) |

Every source is normalized to the same columns (`action`, `rule`, `ruleSet`, `ruleSetVersion`, `ip`, `uri`, `host`, `tx`, `policyId`, `policyScope`, `policyMode`, `resourceId`, `category`) through `column_ifexists`, so a workspace whose schema never saw a field (older gateways, no WAF policy) still answers. Two units everywhere:

- **events** — log rows; one request fires N rules (`Matched` rows + the final decision).
- **requests** — `dcount` of the transaction id (`transactionId_g` / `TransactionId` / `trackingReference`), i.e. what a reader calls "an attack"; falls back to `ip|timestamp|uri` when the id is missing. `blockedRequests` / `detectedRequests` / `allowedRequests` / `challengedRequests` split them by final action (`Blocked|Block`, `Detected|Log`, `Allowed|Allow`, `*Challenge*`).

`category` is the rule-id prefix (`942` SQLi, `941` XSS, `930` LFI, …; 8-digit Microsoft threat-intel ids keep 5 chars: `99001` CVEs, `99005` web shells), prefixed `bot:` for Bot Manager ids (`bot:100` bad, `bot:200` good, `bot:300` unknown — the plain `200` prefix collides with DRS General), or `custom:<name>` for custom rules.

Per source: `summary` (one object: events, requests, blocked/detected/allowed/challenged requests, distinctIps, distinctHosts, activeDays, firstEvent, lastEvent), `byAction`, `byPolicy` (by `resourceId` + `policyId` + `policyScope`, with `mode` when the log carries it — the join key to the ARM inventory), `byCategory`, `byHost` (top N), `topRules` (with `category`, `distinctIps`, `distinctUris`, one sample message), `topClientIps` (with `activeDays`, `distinctHosts`, `distinctUris`, first/last seen), `topUris`, `fpCandidates` (rule×URI combos holding ≥ 80 % of the rule's events with ≥ 50 events, excluding the 949/980 anomaly-decision rules, with `distinctIps`, `blockedHits`, `detectedHits`, `ruleTotal`, `sharePct`), `dailyTrend` (events / requests / blocked / detected per day).

## When to use

- Any run against a subscription with WAF policies or App Gateway/Front Door WAF and diagnostics wired to Log Analytics.

## When NOT to use

- No Log Analytics workspace — omit `--workspace-id`; the stage writes an empty payload, exits 0, and the WAF sub-report degrades to inventory + cost with "—" activity.

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
| `--start` / `--end` | yes | Analysis window (normally the protagonist month). Stage 9 draws every day of this window in the daily chart; days without rows show as "sin logs" (retention gap). |
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
- stderr: `[fetch-waf-logs]` per-source `events / requests (blocked) over N day(s)`; per-query failures are isolated (a missing table just skips that source).

## Side effects

- Reads: nothing local.
- Writes: `{stage-dir}/waf-logs.json`.
- Network: Log Analytics (`api.loganalytics.io` via `Azure.Monitor.Query`), ~10 aggregate queries per source that has data.
