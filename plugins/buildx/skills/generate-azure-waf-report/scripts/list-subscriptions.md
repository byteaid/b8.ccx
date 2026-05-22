# list-subscriptions

One-line summary: enumerate Azure subscriptions reachable by the current credential and stage the result as `subscriptions.json`.

## Purpose

Stage 1 of the WAF report pipeline. Uses `Azure.ResourceManager.ArmClient` with `DefaultAzureCredential` to list every subscription the operator can read, optionally filtered by a regex. Output feeds stage 2 (`discover-resources`) when no `--subscription` argument is passed there.

## When to use

- Operator does not know which subscriptions the credential has access to.
- Driving an end-to-end run from scratch and the staging directory is empty.
- Building a multi-subscription report and you want the canonical id ↔ name mapping recorded once.

## When NOT to use

- The subscription ID is already known and the operator wants to skip ahead — pass `--subscription <id>` directly to `discover-resources` instead.

## Invocation

```bash
dotnet run ${CLAUDE_SKILL_DIR}/scripts/list-subscriptions.cs -- \
  --stage-dir ./run-2026-04-28
```

```bash
# pipe straight to jq, no staging
dotnet run ${CLAUDE_SKILL_DIR}/scripts/list-subscriptions.cs -- \
  --output - --filter "^prod-"
```

## Arguments

| Name | Required | Description |
|---|---|---|
| `--stage-dir` | one of `--stage-dir`/`--output` | Staging directory; writes `subscriptions.json` inside. |
| `--filter` | no | Regex against the subscription display name (case-insensitive). |
| `--output` | one of `--stage-dir`/`--output` | Override output path. `-` writes JSON to stdout. |
| `--force` | no | Overwrite `subscriptions.json` if it already exists. |

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Success. |
| `1` | Generic failure (write conflict, ARM enumeration failure). |
| `3` | `DefaultAzureCredential` failed. Operator must `az login`. |

## Stdout / stderr contract

- stdout: `subscriptions.json` content when `--output -`; otherwise silent.
- stderr: progress and error lines prefixed with `[list-subscriptions]`.

## Side effects

- Reads: nothing local; queries Azure ARM.
- Writes: `{stage-dir}/subscriptions.json` (atomic) when `--stage-dir` is set.
- Network: `https://management.azure.com/`.

## Examples

```bash
dotnet run ${CLAUDE_SKILL_DIR}/scripts/list-subscriptions.cs -- --stage-dir ./run --filter "prod"
# → exit 0, writes ./run/subscriptions.json with the matching subscriptions
```
