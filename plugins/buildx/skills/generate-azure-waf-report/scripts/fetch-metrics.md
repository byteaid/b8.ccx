# fetch-metrics

One-line summary: query Azure Monitor metrics for every resource that matches a rules file and stage them as `metrics.json`.

## Purpose

Stage 4. Replicates the per-rule metric loop in `DataAcquisitionService.AcquireData`. Each rule binds a `cloudType` (resource provider) to a `metricName`, `granularity`, and `aggregation`. The script loads `resources.json`, finds matching resources, and queries `MetricsQueryClient.QueryResourceAsync` for each pair with bounded parallelism.

## When to use

- Performance Efficiency pillar (CPU/memory utilization).
- Reliability pillar when latency/error metrics are added.

## When NOT to use

- Operator only changed branding/meta — skip ahead to stage 7.

## Invocation

```bash
dotnet run ${CLAUDE_SKILL_DIR}/scripts/fetch-metrics.cs -- \
  --stage-dir ./run-2026-04-28 \
  --start 2026-01-01T00:00:00Z \
  --end 2026-04-28T00:00:00Z
```

## metric-rules.json shape

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

If `--rules` is omitted and `{stage-dir}/metric-rules.json` does not exist, the script uses built-in defaults aligned with the upstream "Default Azure Analysis" template (App Service / SQL / Elastic Pool CPU).

## Arguments

| Name | Required | Description |
|---|---|---|
| `--stage-dir` | yes | Staging directory. |
| `--start` | yes | Period start. |
| `--end` | yes | Period end. |
| `--rules` | no | Path to a custom rules file. |
| `--max-parallel` | no | Max concurrent queries. Default `4`. |
| `--force` | no | Overwrite an existing `metrics.json`. |

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Success. |
| `1` | Write conflict. |
| `2` | Predecessor JSON missing or rules empty. |
| `3` | Azure auth failed. |

## Stdout / stderr contract

- stdout: silent.
- stderr: `[fetch-metrics]` per-failure lines (failures are isolated per (resource, rule)).

## Side effects

- Reads: `{stage-dir}/resources.json`, optional `metric-rules.json`.
- Writes: `{stage-dir}/metrics.json`.
- Network: Azure Monitor.

## Examples

```bash
dotnet run ${CLAUDE_SKILL_DIR}/scripts/fetch-metrics.cs -- --stage-dir ./run --start 2026-01-01T00:00:00Z --end 2026-04-28T00:00:00Z --rules ./custom-rules.json
# → exit 0, writes ./run/metrics.json
```
