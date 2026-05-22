---
name: generate-azure-waf-report
description: Generate a Microsoft Azure Well-Architected Framework (WAF) assessment report against an Azure subscription. WAF here means the Microsoft Well-Architected Framework (Reliability, Security, Cost Optimization, Operational Excellence, Performance Efficiency, Sustainability) — NOT the Azure Web Application Firewall product. The skill orchestrates a fixed pipeline of seven .NET 10 file-based C# scripts that stage extracted data as JSON under a working directory and finally emit a Typst document (and optionally compile to PDF via the `typst` CLI). The pipeline is modeled on the ByteAid.CloudAnalyzer hexagonal flow (Acquire → Process → Render) but split into composable, single-purpose scripts that can be re-run independently.
when_to_use: |
  - User asks for a "Well-Architected Framework report", "WAF assessment", "Azure architecture review report", or to "generate a Typst/PDF assessment of an Azure subscription".
  - User mentions extracting Azure resources / metrics / costs / diagnostic logs and rendering a structured cross-pillar report.
  - User wants to extend, debug, or rerun a single stage of the pipeline (e.g. only re-render Typst from existing staged JSON).
allowed-tools: Bash, Edit, Glob, Grep, Monitor, NotebookEdit, PowerShell, Read, Write
user-invocable: false
---

# Azure Well-Architected Framework Report — Pipeline Reference

L1 index. Drives a 7-stage pipeline that extracts raw Azure inventory + telemetry and renders a Typst-based WAF assessment. Each stage is a `.cs` file-based app under `scripts/` with a paired `.md` contract. Run order is strict but every stage is idempotent and may be re-run without re-running the predecessors as long as the staged JSON it depends on already exists.

## Mental model

The skill replaces a single monolithic Blazor service (`AnalysisService` in ByteAid.CloudAnalyzer) with a chain of small CLIs that share a common **staging directory**. The staging directory is a flat folder per run that holds:

```
{stage-dir}/
  subscriptions.json        # produced by list-subscriptions
  resources.json            # produced by discover-resources
  costs.json                # produced by fetch-costs
  metrics.json              # produced by fetch-metrics
  diagnostic-logs.json      # produced by fetch-diagnostic-logs
  report-sets.json          # produced by build-report-sets
  typst/                    # produced by render-typst
    main.typ
    data.json
    *.typ                   # imports
  output.pdf                # optional, only if `typst` CLI is on PATH
```

All scripts authenticate to Azure via `DefaultAzureCredential` (Azure SDK for .NET), which honours `az login`, environment variables, managed identity, etc. — the operator does not pass credentials on the CLI. Diagnostic logs require a Log Analytics workspace ID; without it, `fetch-diagnostic-logs` is a no-op and the WAF Security/Operational sections degrade gracefully.

Final deliverable is a Typst document. PDF compilation is optional and out of band — when `typst` is on PATH, `render-typst` calls it; otherwise the operator can compile the staged `.typ` bundle later.

## Non-negotiable rules

1. **WAF disambiguation, every time.** The first thing the agent surfaces in any planning/answer is the disambiguation: this skill is for the **Microsoft Well-Architected Framework**. If the user wanted Web Application Firewall, stop and confirm before doing anything.
2. **Stage directory is owned by the run.** Always create a fresh `{stage-dir}` per run unless the operator explicitly asks to resume. Never overwrite a foreign directory.
3. **JSON contracts are the API.** Stages communicate ONLY through the JSON files in the staging directory — no hidden global state, no environment-variable smuggling. A stage that needs new data must extend the producer's contract first.
4. **One script = one purpose.** When a stage grows past ~300 lines or sprouts an unrelated subcommand, split it into a new script before adding more.
5. **`DefaultAzureCredential` only.** No client secrets in scripts, no inline tenant IDs. The operator authenticates out of band (`az login`).
6. **Idempotent and resumable.** Re-running any stage with the same inputs MUST produce the same output. `--force` is the only way to overwrite an existing staged file.
7. **Typst output is text.** The pipeline never directly emits PDF; it emits `.typ` and `data.json`. PDF is a downstream concern handled by `render-typst` when `typst` is on PATH, or by the operator afterwards.
8. **Authoring goes through `dotnet-scripting`.** Every script in this skill is a `dotnet-scripting`-compliant single-file CLI. Do NOT write multi-file `.csproj` projects under `scripts/`.

## Pipeline at a glance

| # | Stage | Script | Reads | Writes |
|---|---|---|---|---|
| 1 | List subscriptions | [list-subscriptions](scripts/list-subscriptions.md) | `DefaultAzureCredential` | `subscriptions.json` |
| 2 | Discover resources | [discover-resources](scripts/discover-resources.md) | `subscriptions.json` (or `--subscription`) | `resources.json` |
| 3 | Fetch costs | [fetch-costs](scripts/fetch-costs.md) | `resources.json`, date range | `costs.json` |
| 4 | Fetch metrics | [fetch-metrics](scripts/fetch-metrics.md) | `resources.json`, metric rules JSON | `metrics.json` |
| 5 | Fetch diagnostic logs | [fetch-diagnostic-logs](scripts/fetch-diagnostic-logs.md) | `resources.json`, LAW workspace, KQL categories | `diagnostic-logs.json` |
| 6 | Build report sets | [build-report-sets](scripts/build-report-sets.md) | every staged JSON above | `report-sets.json` |
| 7 | Render Typst | [render-typst](scripts/render-typst.md) | `report-sets.json`, branding/meta options | `typst/`, optional `output.pdf` |

Stages 3, 4, 5 are independent and may run in parallel once stage 2 has produced `resources.json`. Stage 6 is the join point — it requires every preceding artifact (skipping `diagnostic-logs.json` is allowed and produces a partial WAF report).

## Sub-index

| Topic | File | Load when |
|---|---|---|
| Pipeline contracts in detail — JSON schemas, stage error semantics, partial-run rules, WAF pillar mapping | [pipeline.md](pipeline.md) | Implementing or auditing any stage; debugging why stage N rejects stage N-1's output. |
| Typst output architecture — template layout, `data.json` shape, theming, how Typst sources are bundled | [typst-output.md](typst-output.md) | Editing report layout, adding a new visualization, or porting the existing ByteAid Typst templates into this skill. |

## Quick decision matrix

| Need | What to do |
|---|---|
| Full report from scratch on a new subscription | Run stages 1 → 7 in order; save staging dir for diffs. |
| Re-render only (e.g. branding change) | Run stage 7 only; reuse existing `report-sets.json`. |
| Re-process metrics into report sets after a rule tweak | Run stages 6 and 7. |
| Add a new metric | Extend the metric-rules JSON consumed by stage 4, then re-run stages 4 → 7. |
| Inspect raw inventory only | Run stages 1 → 2 and stop; read `resources.json`. |
| Operator has no Log Analytics workspace | Skip stage 5; stage 6 emits a partial report and stage 7 omits the relevant sections. |
| Operator wants Web Application Firewall analysis | Wrong skill — this is Well-Architected Framework. Confirm with user, then point them at the upstream `ByteAid.CloudAnalyzer` "WAF Usage Analysis" template if that is what they meant. |

## Ground truth (upstream reference)

The pipeline is a deliberate split of the hexagonal C#/Blazor solution at:

```
D:\Source\Repos\ByteAid\Tools\ByteAid.CloudAnalyzer\
```

Mapping from upstream to this pipeline:

| Upstream component | This pipeline |
|---|---|
| `AzureCloudAdapter.GetCloudSubscriptions` | stage 1 |
| `AzureCloudAdapter.GetCloudResources` (basic + enriched) | stage 2 |
| `AzureCloudAdapter.GetAllCloudCosts` | stage 3 |
| `AzureCloudAdapter.GetCloudResourceMetrics` (per-rule loop in `DataAcquisitionService`) | stage 4 |
| `AzureCloudAdapter.GetDiagnosticLogs` (KQL via `LogsQueryClient`) | stage 5 |
| `DataProcessingService.ProcessData` + `InMemoryTemplateRepository` rules | stage 6 |
| `TypstReportExporter` + `ReportDataSerializer` + `Templates/` | stage 7 |

The upstream tree is the **reference implementation**, NOT a runtime dependency. The scripts under this skill are independent file-based apps; they re-implement the parts of the upstream code they need and ignore everything else (UI, in-memory template repository, AI analysis, Blazor host).

## Cross-references

- `dotnet-scripting` — script-shape rules, `#:package` directives, packaging.
- `dotnet-system-commandline` — `RootCommand`, `Option<T>`, `Argument<T>`, `ParseResult`, validators.
- `dotnet-file-based-apps` — `dotnet run file.cs`, AOT defaults, file-based-app lifecycle.
- Live: https://learn.microsoft.com/en-us/azure/well-architected/
- Live: https://learn.microsoft.com/en-us/dotnet/api/azure.identity.defaultazurecredential
- Live: https://learn.microsoft.com/en-us/dotnet/api/azure.monitor.query
- Live: https://typst.app/docs/
- Repo rules: `AGENTS.md` § Skills.
