# fetch-resilience

One-line summary: fetch backup coverage, certificate expiry, autoscale settings, SQL/storage protection policies, and top-level availability zones into `resilience.json`.

## Purpose

Stage 8. Feeds the **Fiabilidad**, **Disponibilidad**, and **Rendimiento** sections with nine independent sub-fetches (each degrades on its own — a failure leaves an empty list plus a stderr note, never a failed stage):

- **Backups** — Recovery Services vaults + each vault's `backupProtectedItems` (source resource id, protection state, last recovery point). `build-usage-report` diffs this against the VM inventory to find unprotected VMs.
- **Certificates** — App Service certificates (`Microsoft.Web/certificates`) with `expirationDate`; the build stage buckets them (Vencido / ≤30d Crítico / ≤60d Advertencia). App Gateway listener certs with embedded `publicCertData` are parsed by `build-usage-report` (X509); Key Vault refs are resolved by the **Key Vault certificates** sub-fetch below.
- **Autoscale** — `Microsoft.Insights/autoscaleSettings` with target resource and min/max capacity; the build stage evaluates scaling posture (plans, VMSS) against them.
- **SQL backup policies** — per SQL Database (from `resources.json`, `master` excluded): short-term PITR `retentionDays` + long-term retention (weekly/monthly/yearly; `PT0S` = not configured). The build stage flags databases without LTR.
- **Storage blob services** — per storage account (from `resources.json`): blob/container soft delete + versioning. The build stage flags accounts without blob soft delete.
- **Resource zones** — provider-level listings for the types whose `zones` array is top-level and therefore missing from `resources.json` (VMs incl. `availabilitySetId`, VM Scale Sets, Redis, Application Gateways). Feeds the redundancy posture.
- **Azure Files shares** — management-plane listing of `fileServices/default/shares` per storage account; the build stage matches them against Recovery Services protected items (accountId + share name) for the backup family.
- **App Service backups** — per non-function site: `POST {siteId}/config/backup/list` (needs Contributor, `Microsoft.Web/sites/config/list/action`). 200 → `Configured` (+ retention/frequency), 404 → `NotConfigured`, 401/403/other → `NotVerifiable`.
- **Key Vault certificates** — for each `keyVaultSecretId` referenced by an App Gateway listener cert: data-plane `GET` on the secret (scope `https://vault.azure.net`, needs Key Vault Secrets User or an access policy). ONLY `attributes.exp` is staged as `expirationDate` — the secret value is read over the wire but never persisted. Denied/missing → entry without `expirationDate` (build stage renders "No verificable").

## When to use

- Every full run. **Assume Contributor or higher** — the App Service backup check (`config/backup/list`) and the Key Vault certificate read are always ATTEMPTED; when access is denied the affected entries are staged as `NotVerifiable` / without expiry (never a failed stage), and everything else works with Reader. Run AFTER stage 2 — the SQL/storage/web-app/Key-Vault sub-fetches enumerate from `resources.json` and are skipped (with a stderr note) when it is absent.

## When NOT to use

- Never skip deliberately — graceful degradation covers subscriptions without vaults/certs/autoscale/policies.

## Invocation

```bash
dotnet run ${CLAUDE_SKILL_DIR}/scripts/fetch-resilience.cs -- --stage-dir ./run-2026-06
```

## Arguments

| Name | Required | Description |
|---|---|---|
| `--stage-dir` | yes | Staging directory. |
| `--subscription` | no | Subscription id; falls back to `resources.json` / `subscriptions.json`. |
| `--force` | no | Overwrite an existing `resilience.json`. |

## Output shape

```json
{
  "subscriptionId": "...",
  "vaults": [ { "id": "...", "name": "vault1" } ],
  "protectedItems": [ { "vault": "vault1", "friendlyName": "vm1", "sourceResourceId": "/subscriptions/...", "protectionState": "Protected", "lastBackupTime": "..." } ],
  "webCertificates": [ { "name": "cert1", "expirationDate": "2026-08-02T00:00:00Z", "issuer": "DigiCert", "hostNames": ["www..."] } ],
  "autoscaleSettings": [ { "name": "as1", "targetResourceUri": "/subscriptions/...", "enabled": true, "minCapacity": "1", "maxCapacity": "5" } ],
  "sqlBackupPolicies": [ { "databaseId": "/subscriptions/.../databases/db1", "retentionDays": 7, "weeklyRetention": "PT0S", "monthlyRetention": "PT0S", "yearlyRetention": "PT0S" } ],
  "storageBlobServices": [ { "accountId": "/subscriptions/.../storageAccounts/st1", "blobSoftDeleteEnabled": true, "blobSoftDeleteDays": 7, "containerSoftDeleteEnabled": false, "versioningEnabled": false } ],
  "resourceZones": [ { "id": "/subscriptions/.../virtualMachines/vm1", "zones": ["1"], "availabilitySetId": null } ],
  "fileShares": [ { "accountId": "/subscriptions/.../storageAccounts/st1", "shareName": "share1" } ],
  "webAppBackups": [ { "siteId": "/subscriptions/.../sites/app1", "status": "Configured", "retentionDays": 30, "frequency": "1 Day" } ],
  "keyVaultCertificates": [ { "secretId": "https://kv1.vault.azure.net/secrets/cert1", "expirationDate": "2026-11-01T00:00:00Z" } ]
}
```

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Success (partial sub-fetch failures included). |
| `1` | Write conflict. |
| `2` | No subscription resolvable. |
| `3` | Azure auth failed. |

## Side effects

- Reads: `{stage-dir}/resources.json` (subscription fallback + SQL/storage/site/App-Gateway enumeration) or `subscriptions.json` (subscription fallback).
- Writes: `{stage-dir}/resilience.json`.
- Network: `https://management.azure.com/` + `https://*.vault.azure.net/` (Key Vault cert expiry).
