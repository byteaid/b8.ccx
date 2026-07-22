# build-usage-report

One-line summary: pure-CPU aggregation — join every staged JSON into `usage-report.json` (6 sections: costos, seguridad, fiabilidad, disponibilidad, rendimiento, operacion + deterministic `signals`).

## Purpose

Stage 9, the pipeline's join point. No Azure calls. Reads `resources.json` (required) plus `costs.json`, `metrics.json`, `waf-logs.json`, `advisor.json`, `security.json`, `resilience.json` (all optional — a missing input thins its section, never fails the stage). Emits the section/block structure the renderer consumes and the `signals` block that grounds the narrative.

**All classification is decided here, in code** (never by the narrative agent), and gap severity is **environment-weighted**: each resource is classified Prod / Dev/Test / unclassified (tags `environment`/`env`/`entorno`/`ambiente`/`stage` first, then name/RG naming tokens); Dev/Test degrades the severity to `Informativo`, unclassified gets the conservative `Advertencia`.

| Classification | Rule |
|---|---|
| Zombie | CPU idle share (samples < 10%) ≥ 99% |
| Sobredimensionado | idle ≥ 80% OR (avg < 15% AND p95 < 40%) |
| Saturado | samples ≥ 90% share ≥ 5% OR avg ≥ 80% |
| Recurso sin uso | unattached disk / unassociated public IP / App Service Plan with 0 sites |
| Falso positivo WAF (candidato) | one URI holds ≥ 80% of a rule's hits with ≥ 50 events |
| Certificado Vencido / Crítico / Advertencia | expiry < 0d / ≤ 30d / ≤ 60d; App Gateway listener certs parsed from `publicCertData` (X509), Key Vault refs resolved via staged data-plane expiry — No verificable only when access was denied |
| Backup gap | VM / Azure Files share sin Recovery Services / SQL DB sin LTR / storage sin blob soft delete / App Service sin App Service Backup (env-weighted; NotVerifiable → Informativo); Container Apps sin estado → nunca gap; shares de contenido de Function Apps (site + sufijo hex, incl. huérfanas) → familia propia, nunca gap |
| Redundancia | level per resource: Local < Zona (zoneRedundant / ZRS / zones / HA ZoneRedundant) < Regional (GRS/GZRS / Cosmos multi-región); Prod + unclassified require ≥ Zona, Dev/Test satisfied by Local |
| Auditoría SQL | `auditingSettings.state != Enabled` → security-surface count + `signals.security.sqlServersWithoutAuditing` |
| Cobertura WAF | app cuyo hostname/FQDN no está en un backend pool de App Gateway con WAF y con endpoint directo abierto → Crítico (Prod); tras WAF pero endpoint abierto → bypass, Advertencia; restringida/privada o ingress interno → OK; Container App en managed environment interno (`vnetConfiguration.internal`) → sin exposición a internet aunque el ingress sea external → OK |
| Exposición de infra | DB server con regla 0.0.0.0-255.255.255.255 / storage-KV con networkAcls Allow / Cosmos sin restricciones / Redis público → Advertencia (Prod, regla laxa); PNA Disabled / ACLs Deny / reglas específicas → OK |
| Escalamiento | plan Standard+ / VMSS sin autoscale; Container App sin scale rules; SQL aprovisionado fijo → Informativo |

Compaction is inline: resources sharing the same characteristics tuple collapse into ONE row listing their names up to a character budget and closing with "… y N más" in the same cell (`Helpers.NameList`); a different tuple always starts a new row with its own counter — never a standalone "… y N más" row. Visible counts reconcile with `signals`. Every block's `description` is a short Spanish line stating what the table evaluates — the renderer prints it under the block title.

## When to use

- After the acquisition stages (2-8); re-run (with `--force`) whenever any staged input changed.

## Invocation

```bash
dotnet run ${CLAUDE_SKILL_DIR}/scripts/build-usage-report.cs -- --stage-dir ./run-2026-06
```

## Arguments

| Name | Required | Description |
|---|---|---|
| `--stage-dir` | yes | Staging directory. |
| `--force` | no | Overwrite an existing `usage-report.json`. |

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Success. |
| `1` | Write conflict. |
| `2` | `resources.json` missing. |

## Stdout / stderr contract

- stdout: silent.
- stderr: one `[build-usage-report]` summary line (sections, months, metered resources, waf/defender presence).

## Side effects

- Reads: every staged JSON listed above.
- Writes: `{stage-dir}/usage-report.json` (atomic).
- Network: none.

See `pipeline.md` § Stage 9 for the full output schema (sections, blocks, displayTypes, signals).
