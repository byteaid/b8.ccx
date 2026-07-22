# fetch-costs

One-line summary: fetch Azure cost-management data for the subscription, grouped by resource id + charge type, and stage as `costs.json`.

## Purpose

Stage 3 of the pipeline. Raw POST to `Microsoft.CostManagement/query` (api-version 2023-11-01), `ActualCost` with `PreTaxCost` Sum aggregation grouped by `ResourceId` + `ChargeType`. The window is chunked into ≤30-day slices, and within each slice the script **follows `nextLink` to completion** (POSTing the same body with the `$skiptoken`) so no day is silently dropped — there is **no 15000-row cap**. The final chunk reaches `end`. 429 → sleep 10s and retry. Daily granularity is the default; Monthly via `--granularity Monthly`.

Run this over the **full cost window** the case asks for (it may be older than the CPU analysis month — costs have no 93-day retention limit). `cloudId` is the lowercase ARM id; `build-report-sets` joins it to `resources.json` by `id.ToLowerInvariant()`.

## When to use

- The Costos section and every per-resource cost join in the report.
- Operator changes the cost window.

## When NOT to use

- Stage 6 / 7 needs to re-run after a rule tweak — costs.json is already correct, skip this stage.

## Invocation

```bash
dotnet run ${CLAUDE_SKILL_DIR}/scripts/fetch-costs.cs -- \
  --stage-dir ./run-2026-05 \
  --start 2026-03-01T00:00:00Z \
  --end 2026-06-01T00:00:00Z
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
- stderr: `[fetch-costs]` per-chunk row/page counts; 429 throttling notices; per-chunk-page failures (isolated, not fatal).

## Side effects

- Reads: `{stage-dir}/resources.json` or `subscriptions.json` (subscription resolution).
- Writes: `{stage-dir}/costs.json`.
- Network: Azure Cost Management.

## Examples

```bash
dotnet run ${CLAUDE_SKILL_DIR}/scripts/fetch-costs.cs -- --stage-dir ./run --start 2026-03-01T00:00:00Z --end 2026-06-01T00:00:00Z
# → exit 0, writes ./run/costs.json (every day in the window, fully paginated)
```
