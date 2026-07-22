# fetch-metrics

One-line summary: query Azure Monitor metrics for every resource that matches a rules file and stage them as `metrics.json`.

## Purpose

Stage 4. Each rule binds a `cloudType` (ARM type string) to a `metricName`, `granularity`, and `aggregation`. The script loads `resources.json`, matches each rule's `cloudType` against the resource's flat **`type` string** (case-insensitive), reads the ARM id from **`id`**, and queries `MetricsQueryClient.QueryResourceAsync` per (resource, rule) pair with bounded parallelism.

**Defaults use 5-minute granularity** (`00:05:00`) — the light report classifies avg/p95/idle, which does not need 1-minute resolution; a 31-day month yields ≈8,928 samples per series. Azure Monitor caps datapoints per query, so the `[start, end)` window is split into **1-day sub-queries** per (resource, rule) and concatenated, with bounded 429 retry (3 attempts, 10/20/40s backoff).

**93-day retention (IMPORTANT):** Azure Monitor metrics are retained ~93 days. Scope this stage to a **recent full month** within that trailing window (normally the report's protagonist month), independent of the 3-month cost window driving `fetch-costs`. A `--start`/`--end` outside ~93 days returns empty series.

## When to use

- The CPU classification (zombie / oversized in Costos; saturated in Rendimiento).
- Adding a resource family not covered by the defaults (e.g. Container Apps `UsageNanoCores`) via `--rules`.

## When NOT to use

- Operator only changed branding/narrative — skip ahead to stages 9-10.

## Invocation

```bash
dotnet run ${CLAUDE_SKILL_DIR}/scripts/fetch-metrics.cs -- \
  --stage-dir ./run-2026-06 \
  --start 2026-06-01T00:00:00Z \
  --end 2026-07-01T00:00:00Z
```

## metric-rules.json shape

```json
{
  "rules": [
    { "cloudType": "microsoft.web/serverfarms", "metricName": "CpuPercentage", "granularity": "00:05:00", "aggregation": "Average" }
  ]
}
```

If `--rules` is omitted and `{stage-dir}/metric-rules.json` does not exist, built-in defaults apply at **5-minute granularity**: App Service Plan `CpuPercentage` + `MemoryPercentage`, SQL Database / Elastic Pool `cpu_percent`, Virtual Machine `Percentage CPU`.

## Arguments

| Name | Required | Description |
|---|---|---|
| `--stage-dir` | yes | Staging directory. |
| `--start` | yes | Analysis-month start. Keep within Azure Monitor's ~93-day retention. |
| `--end` | yes | Analysis-month end. |
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
- stderr: `[fetch-metrics]` per-series sample counts and per-failure lines (failures are isolated per (resource, rule); 429-exhausted series are skipped and can be backfilled by re-running).

## Side effects

- Reads: `{stage-dir}/resources.json`, optional `metric-rules.json`.
- Writes: `{stage-dir}/metrics.json`.
- Network: Azure Monitor.
