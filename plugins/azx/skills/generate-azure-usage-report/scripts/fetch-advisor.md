# fetch-advisor

One-line summary: fetch every Azure Advisor recommendation for the subscription and stage it as `advisor.json`.

## Purpose

Stage 6. Azure Advisor is Microsoft's own "current best practices" engine; its recommendations (categories `Cost`, `Security`, `HighAvailability`, `OperationalExcellence`, `Performance`) ground the report's recommendation prose in official guidance instead of invented advice. `build-usage-report` distributes them into the matching sections; cost recommendations carry `annualSavingsAmount` when Advisor computed savings.

Endpoint: `GET /subscriptions/{id}/providers/Microsoft.Advisor/recommendations?api-version=2023-01-01` with full `nextLink` pagination and bounded 429 retry.

## When to use

- Every full run — Advisor is free, always-on, and requires only Reader access.

## When NOT to use

- Never skip deliberately; if the call fails the pipeline still works (sections lose their Advisor blocks).

## Invocation

```bash
dotnet run ${CLAUDE_SKILL_DIR}/scripts/fetch-advisor.cs -- --stage-dir ./run-2026-06
```

## Arguments

| Name | Required | Description |
|---|---|---|
| `--stage-dir` | yes | Staging directory. |
| `--subscription` | no | Subscription id; falls back to `resources.json` / `subscriptions.json`. |
| `--force` | no | Overwrite an existing `advisor.json`. |

## Output shape

```json
{
  "subscriptionId": "...", "generatedAt": "...",
  "recommendations": [
    { "category": "Cost", "impact": "High", "impactedField": "Microsoft.Sql/servers/databases",
      "impactedValue": "DbVentas", "problem": "Right-size ...", "solution": "Scale down ...",
      "resourceId": "/subscriptions/...", "annualSavingsAmount": "912", "savingsCurrency": "USD" }
  ]
}
```

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Success. |
| `1` | Write conflict or enumeration failure. |
| `2` | No subscription resolvable. |
| `3` | Azure auth failed. |

## Side effects

- Reads: `{stage-dir}/resources.json` or `subscriptions.json` (subscription fallback).
- Writes: `{stage-dir}/advisor.json`.
- Network: `https://management.azure.com/`.
