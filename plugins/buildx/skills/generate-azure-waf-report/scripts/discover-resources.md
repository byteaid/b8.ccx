# discover-resources

One-line summary: enumerate every resource in the target subscription and stage the inventory as `resources.json`.

## Purpose

Stage 2 of the pipeline. Mirrors `AzureCloudAdapter.GetCloudResources` from the upstream ByteAid solution: pulls every `GenericResource`, captures its type, region, SKU, tags, and raw properties, and writes a normalized JSON inventory. Detailed discovery (per-type enrichment for VMs, App Services, NSGs, App Gateways, WAF policies, DNS zones, Container Apps) is a follow-up; v0.1.0 ships basic discovery only.

## When to use

- Driving the pipeline forward after stage 1.
- Re-discovering resources after the operator has added or removed services.
- Debugging "why is resource X missing from the report?" — stage 6 reads exclusively from this file.

## When NOT to use

- The Azure inventory has not changed since the last run — stage 6 / 7 can re-run against the existing file.

## Invocation

```bash
dotnet run ${CLAUDE_SKILL_DIR}/scripts/discover-resources.cs -- \
  --stage-dir ./run-2026-04-28 \
  --subscription 00000000-0000-0000-0000-000000000000
```

## Arguments

| Name | Required | Description |
|---|---|---|
| `--stage-dir` | yes | Staging directory. |
| `--subscription` | no | Subscription id. Falls back to first entry in `subscriptions.json`. |
| `--detailed` | no | Reserved for per-type enrichment (Reliability/Security pillars). |
| `--filter-system-resources` | no | Drop master DBs and other Microsoft-managed resources. Default `true`. |
| `--force` | no | Overwrite an existing `resources.json`. |

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Success. |
| `1` | Generic failure or write conflict. |
| `2` | No subscription resolvable. |
| `3` | Azure auth failed. |

## Stdout / stderr contract

- stdout: silent on success.
- stderr: `[discover-resources]` progress and error lines.

## Side effects

- Reads: `{stage-dir}/subscriptions.json` (only if `--subscription` is absent).
- Writes: `{stage-dir}/resources.json` (atomic).
- Network: `https://management.azure.com/`.

## Examples

```bash
dotnet run ${CLAUDE_SKILL_DIR}/scripts/discover-resources.cs -- --stage-dir ./run --subscription $SUB
# → exit 0, writes ./run/resources.json
```
