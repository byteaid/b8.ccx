# fetch-costs

One-line summary: fetch Azure cost-management data for the subscription, grouped by resource id + charge type, and stage as `costs.json`.

## Purpose

Stage 3 of the pipeline. Replicates `AzureCloudAdapter.GetAllCloudCosts`: monthly chunked queries against the Cost Management API with `PreTaxCost` aggregation grouped by `ResourceId` and `ChargeType`, plus 429-throttling retries. Daily granularity is the default; Monthly is supported via `--granularity Monthly`.

## When to use

- Cost Optimization pillar in the report.
- Operator changes the analysis window.

## When NOT to use

- Stage 6 / 7 needs to re-run after a rule tweak — costs.json is already correct, skip this stage.

## Invocation

```bash
dotnet run ${CLAUDE_SKILL_DIR}/scripts/fetch-costs.cs -- \
  --stage-dir ./run-2026-04-28 \
  --start 2026-01-01T00:00:00Z \
  --end 2026-04-28T00:00:00Z
```

## Arguments

| Name | Required | Description |
|---|---|---|
| `--stage-dir` | yes | Staging directory. |
| `--subscription` | no | Subscription id; falls back to resources.json/subscriptions.json. |
| `--start` | yes | Period start (ISO-8601). |
| `--end` | yes | Period end (ISO-8601). |
| `--granularity` | no | `Daily` (default) or `Monthly`. |
| `--force` | no | Overwrite an existing `costs.json`. |

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Success. |
| `1` | Write conflict or query failure. |
| `2` | No subscription resolvable. |
| `3` | Azure auth failed. |

## Stdout / stderr contract

- stdout: silent.
- stderr: `[fetch-costs]` progress; throttling notices; per-chunk failures (chunk-isolated, not fatal).

## Side effects

- Reads: `{stage-dir}/resources.json` or `subscriptions.json` (subscription resolution).
- Writes: `{stage-dir}/costs.json`.
- Network: Azure Cost Management.

## Examples

```bash
dotnet run ${CLAUDE_SKILL_DIR}/scripts/fetch-costs.cs -- --stage-dir ./run --start 2026-01-01T00:00:00Z --end 2026-04-28T00:00:00Z
# → exit 0, writes ./run/costs.json
```
