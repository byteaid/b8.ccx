# build-report-sets

One-line summary: pure-CPU aggregation that turns the staged JSONs into pillar-grouped report sets.

## Purpose

Stage 6. No Azure calls. Reads `resources.json`, `costs.json`, `metrics.json`, and `diagnostic-logs.json`, then emits a `report-sets.json` document organized under the six WAF pillars (Reliability, Security, Cost Optimization, Operational Excellence, Performance Efficiency, Sustainability). Each pillar carries one or more `ReportSet` entries shaped like the upstream `Display.ReportSet` minus the runtime-only `Context` dictionary.

## When to use

- Whenever any of the staged source JSONs change.
- After tweaking the aggregation rules embedded in this script.

## When NOT to use

- Operator only changed branding/meta — skip ahead to stage 7.

## Invocation

```bash
dotnet run ${CLAUDE_SKILL_DIR}/scripts/build-report-sets.cs -- \
  --stage-dir ./run-2026-04-28
```

## Arguments

| Name | Required | Description |
|---|---|---|
| `--stage-dir` | yes | Staging directory (must contain at least `resources.json`). |
| `--force` | no | Overwrite `report-sets.json`. |

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Success. |
| `1` | Write conflict. |
| `2` | `resources.json` missing. |

## Stdout / stderr contract

- stdout: silent.
- stderr: a one-line summary on success.

## Side effects

- Reads: `resources.json` (required), `costs.json`, `metrics.json`, `diagnostic-logs.json` (optional).
- Writes: `{stage-dir}/report-sets.json`.
- Network: none.

## Pillar coverage in v0.1.0

| Pillar | Source | Status |
|---|---|---|
| Reliability | resources | partial — SKU tier pie |
| Security | resources, diagnostic-logs | partial — surface counts |
| Cost Optimization | costs | full — monthly trend, top resources |
| Operational Excellence | resources, diagnostic-logs | partial — tag coverage |
| Performance Efficiency | metrics | full — averages per metric |
| Sustainability | costs, resources | placeholder |

## Examples

```bash
dotnet run ${CLAUDE_SKILL_DIR}/scripts/build-report-sets.cs -- --stage-dir ./run
# → exit 0, writes ./run/report-sets.json with six pillars
```
