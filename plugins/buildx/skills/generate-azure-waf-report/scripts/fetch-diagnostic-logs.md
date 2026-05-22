# fetch-diagnostic-logs

One-line summary: query a Log Analytics workspace for diagnostic logs relevant to the WAF report and stage them as `diagnostic-logs.json`.

## Purpose

Stage 5. Optional. Mirrors `AzureCloudAdapter.GetDiagnosticLogs`: runs a templated KQL per category (default set: `AzureActivity`, `AppServiceHTTPLogs`, `ApplicationGatewayAccessLog`, `ApplicationGatewayFirewallLog`) against `LogsQueryClient.QueryWorkspaceAsync`. When `--workspace-id` is missing, writes an empty payload so stage 6 can degrade gracefully.

## When to use

- Operator has Log Analytics centralized for the subscription.
- Security or Operational Excellence pillars require activity / firewall / HTTP logs.

## When NOT to use

- No Log Analytics workspace configured — skip this stage entirely or run with no `--workspace-id` to emit the empty payload.

## Invocation

```bash
dotnet run ${CLAUDE_SKILL_DIR}/scripts/fetch-diagnostic-logs.cs -- \
  --stage-dir ./run-2026-04-28 \
  --workspace-id 11111111-1111-1111-1111-111111111111 \
  --start 2026-04-21T00:00:00Z --end 2026-04-28T00:00:00Z \
  --category AzureActivity --category ApplicationGatewayFirewallLog
```

## Arguments

| Name | Required | Description |
|---|---|---|
| `--stage-dir` | yes | Staging directory. |
| `--workspace-id` | no | Log Analytics workspace GUID. Without it, an empty payload is staged. |
| `--start` | yes | Period start. |
| `--end` | yes | Period end. |
| `--category` | no | KQL Category filter. Repeatable. Defaults to a WAF-relevant set. |
| `--limit` | no | Max rows per category. Default `10000`. |
| `--force` | no | Overwrite an existing `diagnostic-logs.json`. |

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Success (including the no-workspace empty payload). |
| `1` | Write conflict. |
| `3` | Azure auth failed. |

## Stdout / stderr contract

- stdout: silent.
- stderr: per-category errors and "no workspace" warning.

## Side effects

- Reads: nothing local.
- Writes: `{stage-dir}/diagnostic-logs.json`.
- Network: Log Analytics API.

## Examples

```bash
dotnet run ${CLAUDE_SKILL_DIR}/scripts/fetch-diagnostic-logs.cs -- --stage-dir ./run --start 2026-04-21T00:00:00Z --end 2026-04-28T00:00:00Z
# → exit 0, writes empty payload because --workspace-id was not supplied
```
