# fetch-security

One-line summary: fetch the Defender for Cloud security posture (secure score + unhealthy assessments) into `security.json`.

## Purpose

Stage 7. Feeds the **Seguridad** section:

- **Secure score** — `GET .../Microsoft.Security/secureScores?api-version=2023-01-01` (the built-in `ascScore` initiative).
- **Assessments** — `GET .../Microsoft.Security/assessments?api-version=2021-06-01&$expand=metadata`, counting all and keeping only `Unhealthy` ones (with severity from metadata), capped by `--max-items`.

When Defender for Cloud is unavailable (not enabled, 403/404) the stage writes `present: false` and exits 0 — the Seguridad section then rests on Advisor Security recommendations plus the surface derived from `resources.json`.

## When to use

- Every full run; requires only Reader (Security Reader) access. Works with the free Foundational CSPM tier.
- Also stages the per-server SQL auditing state (`auditingSettings/default`, servers enumerated from `resources.json`) as `sqlAuditing: [{ serverId, name, state }]` — the build stage surfaces servers whose auditing is not `Enabled` in the Superficie Expuesta block and in `signals.security.sqlServersWithoutAuditing`.
- Also stages the per-site network exposure summary (`config/web`: `ipSecurityRestrictions` + `publicNetworkAccess`, sites enumerated from `resources.json`) as `webAccessRestrictions: [{ siteId, name, publicNetworkAccess?, restrictionCount, denyAll, openToAll }]` — input for the build stage's WAF-coverage check (`waf-exposure` block: apps whose public traffic does not pass through a WAF-fronted gateway or whose direct endpoint stays open).
- Also stages the database-server firewall rules (SQL + MySQL/PostgreSQL flexible, enumerated from `resources.json`) as `dbFirewallRules: [{ serverId, name, startIpAddress, endIpAddress }]` — the `0.0.0.0-255.255.255.255` rule is the any-origin marker for the build stage's infra-exposure check (`infra-exposure` block).

## When NOT to use

- Never skip deliberately — the graceful degradation handles absent Defender.

## Invocation

```bash
dotnet run ${CLAUDE_SKILL_DIR}/scripts/fetch-security.cs -- --stage-dir ./run-2026-06
```

## Arguments

| Name | Required | Description |
|---|---|---|
| `--stage-dir` | yes | Staging directory. |
| `--subscription` | no | Subscription id; falls back to `resources.json` / `subscriptions.json`. |
| `--max-items` | no | Cap on unhealthy assessments kept. Default `500`. |
| `--force` | no | Overwrite an existing `security.json`. |

## Output shape

```json
{
  "subscriptionId": "...", "present": true,
  "secureScore": { "current": 21.5, "max": 43, "percentage": 50.0 },
  "assessmentCounts": { "total": 120, "unhealthy": 18, "bySeverity": { "High": 4, "Medium": 9, "Low": 5 } },
  "unhealthyAssessments": [ { "displayName": "...", "severity": "High", "resourceId": "/subscriptions/..." } ]
}
```

`secureScore.percentage` is staged already ×100 (e.g. `50.0`, not `0.5`).

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Success (including `present: false`). |
| `1` | Write conflict. |
| `2` | No subscription resolvable. |
| `3` | Azure auth failed. |

## Side effects

- Reads: `{stage-dir}/resources.json` or `subscriptions.json` (subscription fallback).
- Writes: `{stage-dir}/security.json`.
- Network: `https://management.azure.com/`.
