---
name: generate-azure-usage-report
description: Generate a compact monthly Azure usage report ("reporte light") for a subscription — how Azure was used over the last 3 months with the LAST month as protagonist (prior months are comparison baseline only). Sections; executive summary for leadership, consolidated costs (spend trend, top consumers, zombie/oversized and unused infrastructure, Advisor savings), security posture (Defender for Cloud + WAF coverage per app — direct-traffic/bypass detection, mandatory WAF for Prod apps — + the Web Application Firewall sub-report — the FIREWALL product, not the Well-Architected Framework), reliability (backup coverage per family — VMs, SQL PITR/LTR, Cosmos, flexible servers, storage soft delete, Azure Files, Container Apps, App Service Backup), availability (TLS certificate expiry incl. App Gateway listeners resolved against Key Vault, plus one redundancy posture per resource — levels Local/Zona/Regional, prod asks for at least zone), performance (saturated resources + scaling rules across compute and databases), and operational hygiene (naming, tagging, fragmentation). Every finding is environment-weighted (Prod vs Dev/Test, from tags + naming) and every section closes with recommendations grounded in Azure Advisor. Pipeline of .NET 10 file-based C# scripts staging JSON under a working directory, one agent-authored Spanish narrative step, and a Typst render (optional PDF via the `typst` CLI).
when_to_use: |
  - "Azure usage report", "reporte de uso", "reporte mensual light", monthly usage/cost/security status of an Azure subscription for a client or leadership.
  - Detecting oversized/zombie/unused resources, summarizing WAF attacks and false positives, expiring certificates (App Service + App Gateway listeners), backup gaps, redundancy-level gaps (Local/Zona/Regional), scaling gaps, tagging/naming issues — compiled into one compact PDF.
  - Extending, debugging, or re-running a single stage of the usage-report pipeline.
  - NOT for a deep Well-Architected Framework assessment (that is the separate WAF-report pipeline).
allowed-tools: Bash, PowerShell, Read, Write
user-invocable: false
---

# Azure Usage Report (Light) — Pipeline Reference

L1 index. Drives a 10-stage pipeline: extract Azure inventory + telemetry + posture (stages 1-8), aggregate into a compact 6-section report with deterministic `signals` (stage 9), have the operating agent author a Spanish `narrative.json` (the one non-script step), and render a themed Typst document (stage 10). Every script stage is a `.cs` file-based app under `scripts/` with a paired `.md` contract; run order is strict but every stage is idempotent and re-runnable as long as the staged JSON it depends on exists.

## Report shape

**Compact and precise by design** — the deliverable targets ~10-15 pages: only problems and their recommendations, never resource-by-resource dumps. The report centers on the **current month** (`signals.currentMonth`, the last month in the dataset); the two preceding months appear only as comparison columns. Sections, in order:

1. **Resumen Ejecutivo** — narrative for leadership: estado general, ATENCIÓN callout, 4-KPI row (costo del mes, variación MoM, Secure Score, recursos), hallazgos clave, matriz de riesgos, próximos pasos.
2. **Costos** — monthly cost trend + top service types + top resources (current-month ranked), CPU classification, Zombie/Sobredimensionado resources (with environment), unused resources (unattached disks, free public IPs, empty plans), Advisor Cost recommendations with savings.
3. **Seguridad** — Defender secure score, top unhealthy findings by severity, exposed surface (incl. SQL servers without auditing enabled), the WAF-coverage check (apps receiving direct public traffic without passing through the WAF, or with a bypassable direct endpoint — mandatory for Prod apps; Container Apps on an internal managed environment count as not internet-exposed), the infra-exposure check (DB servers, storage, Key Vault, Cosmos, Redis accepting any-origin traffic — laxer rule: Advertencia on Prod), then the WAF sub-report (inventory with mode Prevention/Detection, events by action, top rules/IPs, false-positive candidates), Advisor Security recommendations.
4. **Fiabilidad** — goes straight to the per-resource protection gaps (families evaluated: VMs via Recovery Services, SQL PITR + LTR, Cosmos, MySQL/PostgreSQL flexible, storage blob soft delete, Azure Files shares vs Recovery Services — Function-App content shares split out as redeployable, Container Apps as stateless-by-design, App Service Backup via `config/backup/list`); the family coverage summary lives in the block intro and `signals.reliability.families` (no coverage table), then Advisor Reliability recommendations.
5. **Disponibilidad** — TLS certificate expiry merged from App Service and App Gateway listeners (Key Vault refs resolved via data-plane read; "No verificable" only when access was denied), and ONE redundancy posture table: each resource gets a level (Local < Zona < Regional) with Prod/unclassified asked for at least Zona and Dev/Test satisfied by Local.
6. **Rendimiento** — saturated resources (CPU), scaling rules across compute + databases (autoscale on plans/VMSS, Container Apps scale rules, SQL serverless/elastic), Advisor Performance recommendations.
7. **Operación** — tagging coverage + inconsistent tag keys, fragmentation (RGs/regions/SKUs), naming-convention split, Advisor Operational recommendations.

Every content section carries a Spanish Análisis block (Resumen / Observaciones / **Recomendaciones**) authored by the agent in `narrative.json`, grounded in `signals` and the staged Advisor guidance.

## Mental model

Stages communicate ONLY through JSON files in a flat per-run **staging directory**:

```
{stage-dir}/
  subscriptions.json      # 1 list-subscriptions
  resources.json          # 2 discover-resources
  costs.json              # 3 fetch-costs      (3-month cost window)
  metrics.json            # 4 fetch-metrics    (analysis month, 5-min CPU/memory)
  waf-logs.json           # 5 fetch-waf-logs   (KQL-aggregated firewall events)
  advisor.json            # 6 fetch-advisor    (best-practice recommendations)
  security.json           # 7 fetch-security   (Defender score + assessments)
  resilience.json         # 8 fetch-resilience (backups, certs, autoscale, SQL/storage policies, zones)
  usage-report.json       # 9 build-usage-report (sections + signals)
  narrative.json          # AUTHORED BY THE AGENT (Spanish)
  typst/                  # 10 render-typst (main.typ, report.typ, data.json)
  output.pdf              # optional, when `typst` CLI is on PATH and --compile set
```

All scripts authenticate via `DefaultAzureCredential` (honours `az login`, env vars, managed identity). **Assume Contributor or higher** (plus Key Vault data-plane read for listener certs): the privileged checks — App Service backup config (`config/backup/list`), Key Vault certificate expiry — are ALWAYS attempted first; only when access is actually denied do the affected rows degrade to "No verificable" (the out-of-scope fallback), and the rest of the pipeline still works with Reader. Stages 5-8 degrade gracefully: no workspace → the WAF blocks are inventory-only; no Defender → Seguridad rests on Advisor + surface; no vaults/certs/policies → the affected blocks render "Sin hallazgos" or "no verificado", never vanish.

**Two analysis windows.** Costs cover the full 3-month window; metrics cover ONE recent full month (Azure Monitor retains ~93 days — an older month returns empty series). `build-usage-report` records both (`months` and `analysisMonth`).

## Non-negotiable rules

1. **Light means light.** Sections carry problems + recommendations, capped tables (top-N), charts where they replace a table with advantage, and one Análisis per section. Never re-introduce per-resource dumps or per-block analysis — that is the WAF-report pipeline's job.
1b. **Fixed template.** Section ids, block ids, order, and caps are IDENTICAL on every run (see `pipeline.md` § Fixed template); an empty block renders "Sin hallazgos", never disappears. This is what makes consecutive monthly reports directly comparable — do not add, drop, or reorder blocks per run.
1c. **Counts always reconcile, compaction is inline.** Resources sharing the same characteristics (type/environment/finding/severity…) collapse into ONE row that lists their names inline and closes with "… y N más" IN THE SAME CELL — never a standalone "… y N más" row; a different characteristics tuple always starts a new row with its own counter. A summary figure must never exceed what the detail table accounts for.
2. **Stage directory is owned by the run.** Fresh `{stage-dir}` per run unless the operator explicitly resumes. Never overwrite a foreign directory.
3. **JSON contracts are the API.** No hidden state between stages; a stage needing new data extends the producer's contract first.
4. **Classification is code, not prose.** Zombie/oversized/saturated thresholds, FP candidates, cert buckets, backup gaps, zone/regional posture, scaling gaps AND the environment weighting are decided in `build-usage-report` — the narrative only phrases what `signals` states. No invented figures.
4b. **Findings are environment-weighted.** Each resource is classified Prod / Dev/Test / unclassified (tags `environment`/`env`/`entorno`/`ambiente`/`stage` first, then name/RG naming tokens); gap severity degrades to `Informativo` on Dev/Test in code. The narrative NEVER escalates an Informativo finding — a dev/uat resource without zone redundancy or backup is a deliberate trade-off to mention as context, not a grave finding. Validate the data before asserting: unclassified resources get the conservative middle severity and should be flagged for classification, not dramatized.
5. **Recommendations follow Azure guidance.** Advisor and Defender recommendations are staged and cited; own checks (certs, backups, tags, naming) map to documented Azure best practices (CAF naming/tagging, Azure Backup, autoscale).
6. **`DefaultAzureCredential` only.** No secrets in scripts or args; authenticate out of band. For multi-tenant operators, bind the run by setting `AZURE_CONFIG_DIR` to the target tenant's Azure CLI profile dir inline on every script invocation.
7. **Idempotent and resumable.** Same inputs → same output; `--force` is the only overwrite path.
8. **The narrative is the agent's job.** After stage 9, author `narrative.json` with the FROZEN prompt in [narrative-stage.md](narrative-stage.md) used verbatim — that is what keeps monthly runs homogeneous.
9. **Whitelabel by default.** Without a `.styles/` style guide (`--styles DIR`, auto-discovered at `{stage-dir}/.styles`) the deliverable is neutral and brand-free: gray palette, default font, no company, no author, no logo. ALL branding enters through `.styles/` (`theme.json` + logo + optional `fonts/` with the brand typeface) or explicit flags — see [typst-output.md](typst-output.md) § Theming. A style guide changes appearance only, never structure.
10. **Typst output is text.** PDF is downstream (`--compile` when `typst` is on PATH, or manual).

## Pipeline at a glance

| # | Stage | Script / actor | Needs | Writes |
|---|---|---|---|---|
| 1 | List subscriptions | [list-subscriptions](scripts/list-subscriptions.md) | credential | `subscriptions.json` |
| 2 | Discover resources | [discover-resources](scripts/discover-resources.md) | `subscriptions.json` or `--subscription` | `resources.json` |
| 3 | Fetch costs | [fetch-costs](scripts/fetch-costs.md) | `resources.json`, 3-month window | `costs.json` |
| 4 | Fetch metrics | [fetch-metrics](scripts/fetch-metrics.md) | `resources.json`, analysis month | `metrics.json` |
| 5 | Fetch WAF logs | [fetch-waf-logs](scripts/fetch-waf-logs.md) | workspace id (optional) | `waf-logs.json` |
| 6 | Fetch Advisor | [fetch-advisor](scripts/fetch-advisor.md) | subscription | `advisor.json` |
| 7 | Fetch security | [fetch-security](scripts/fetch-security.md) | subscription | `security.json` |
| 8 | Fetch resilience | [fetch-resilience](scripts/fetch-resilience.md) | subscription (+ `resources.json` for SQL LTR / storage soft-delete checks) | `resilience.json` |
| 9 | Build report | [build-usage-report](scripts/build-usage-report.md) | stages 2-8 staged JSON | `usage-report.json` |
| — | **Author narrative** | **the operating agent** | `usage-report.json` (esp. `signals`) | `narrative.json` |
| 10 | Render Typst | [render-typst](scripts/render-typst.md) | `usage-report.json`, `narrative.json` | `typst/`, optional `output.pdf` |

Stages 3-8 are independent and may run in parallel once stage 2 staged `resources.json`. Stage 9 is the join point (only `resources.json` is hard-required; everything else degrades). The narrative step sits strictly between 9 and 10.

## Sub-index

| Topic | File | Load when |
|---|---|---|
| Stage contracts — `usage-report.json` schema (sections/blocks/displayTypes), `signals`, classification thresholds, narrative schema, error semantics | [pipeline.md](pipeline.md) | Implementing/auditing a stage; authoring the narrative; debugging a stage rejection. |
| Narrative stage — the frozen Spanish-analysis prompt and the determinism split | [narrative-stage.md](narrative-stage.md) | Authoring `narrative.json` (use verbatim) or auditing run-to-run consistency. |
| Typst output — layout, theming flags, per-displayType rendering, status-token coloring | [typst-output.md](typst-output.md) | Changing branding or report layout. |

## Quick decision matrix

| Need | What to do |
|---|---|
| Full report on a subscription | 1 → 2, then 3-8 (parallel; 3 over the 3-month window, 4-5 over the analysis month), 9, author narrative, 10. |
| Re-render only (branding/narrative tweak) | Edit `narrative.json` or flags, run stage 10 with `--force`. |
| Re-classify after a threshold or input change | Stage 9 `--force`, re-author narrative, stage 10. |
| No Log Analytics workspace | Skip `--workspace-id` in stage 5 — the WAF blocks in Seguridad become inventory + prerequisite note. |
| SQL LTR / storage soft-delete rows say "not verified" | Re-run stage 8 AFTER stage 2 so `resources.json` is staged (those sub-fetches enumerate from it). |
| No Defender for Cloud | Nothing to do — stage 7 writes `present:false`, section degrades to Advisor + surface. |
| Add a resource family to CPU analysis | Supply `--rules` to stage 4 (e.g. Container Apps), re-run 4 → 9 → narrative → 10. |
| Deep architecture assessment requested | Wrong skill — that is the Well-Architected Framework report pipeline, not this light usage report. |

## Cross-references

- `azure-pricing-api` — when the report's findings feed a re-sizing quote.
- `generate-azure-solution-proposal` — sibling azx deliverable skill; shares the Typst-first, themeable-deliverable pattern.
- Live: https://learn.microsoft.com/en-us/azure/advisor/advisor-overview
- Live: https://learn.microsoft.com/en-us/azure/defender-for-cloud/secure-score-security-controls
- Live: https://learn.microsoft.com/en-us/azure/web-application-firewall/
- Live: https://learn.microsoft.com/en-us/azure/cloud-adoption-framework/ready/azure-best-practices/resource-naming
- Live: https://typst.app/docs/
- Repo rules: `AGENTS.md` § Skills.
