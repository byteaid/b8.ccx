---
name: generate-azure-solution-proposal
description: Deterministic procedure to produce an Azure solution proposal document (Typst → PDF) with a fixed 9-section anatomy — Resumen ejecutivo, Antecedentes, Propuesta de solución, Arquitectura (icon diagram + resource-role table), Costo de infraestructura (live dated prices), Esfuerzo (hours by task, no dates), Cronograma (dated schedule + milestones), Pre requisitos y supuestos, Fuera de alcance. Consumes `azure-pricing-api` for every cost figure, `byteaid-assets-icons` for every diagram icon (resolution + download), and `azure-diagrams` for authoring the Arquitectura diagram itself. White-label by default — the deliverable carries zero brand or tooling references; an optional `.styles/` directory supplies ALL branding (logo, company, palette) and themes the resulting Typst without altering structure. Same inputs → same document structure, file layout, and section anatomy on every run.
when_to_use: |
  - User asks for an Azure solution proposal, project proposal, client proposal, cotización de proyecto, propuesta de solución/implementación, or a pre-sales document combining architecture + cost + effort + schedule.
  - User asks to regenerate or restyle an existing proposal (style guide change, scope change, re-price).
  - NOT for a standalone quote (use `azure-pricing-api` § quoting-recipes directly) or a standalone diagram (use `azure-diagrams`).
allowed-tools: Bash, PowerShell, Glob, Grep, Read, Write, Edit, WebFetch
user-invocable: false
---

# Generate Azure Solution Proposal

L1 index. A proposal is ONE Typst document compiled to PDF with EXACTLY nine sections in fixed order. Nothing about the anatomy is negotiable; only content and theme vary. Execute the pipeline below top-to-bottom — no reordering, no skipping.

## Inputs (collect ALL before writing anything)

Ask the user ONCE for every missing item; blocking items stop the pipeline until answered.

| Input | Blocking | Default |
|---|---|---|
| Client / project name | yes | — |
| Problem context (feeds Antecedentes) | yes | — |
| Workload description + expected scale | yes | — |
| Azure region(s) | yes | — |
| Cronograma start date | yes | — |
| Team capacity | no | 1 person × 8 h/business-day |
| Currency | no | USD |
| Output language | no | Spanish (canonical titles) |
| `.styles/` directory (theme, logo, fonts) | no | none — neutral, brand-free document |
| Output directory | no | `./proposal/` |

## Pipeline (fixed order)

1. **Collect inputs** per the table above. Do not start step 2 with blocking items open.
2. **Design the solution.** Map workload components to Azure services / SKUs / tiers / regions. Record every billable component in a component inventory (one line: service, SKU, region, monthly quantity assumption) — this inventory is the single source for BOTH the Arquitectura table and the Costo table; never derive them independently.
3. **Price.** For each inventory line, follow `azure-pricing-api` end to end (discovery → exact filters → full pagination → `Consumption` base + labeled commitment alternatives → record `meterId` + retrieval date). One currency, one retrieval date for the whole document.
4. **Resolve icons.** For each distinct service in the inventory, resolve + verify its slug per `byteaid-assets-icons` and download to `{output-dir}/.assets/icons/{slug}.svg` — every downloaded resource lands under `.assets/`, never at the root. One slug per service for the whole document. (The diagram that consumes these icons is authored per `azure-diagrams` in step 8.)
5. **Estimate effort.** Decompose delivery into tasks `T01..Tnn` (stable zero-padded ids, dependency order), hours per task, summed total. No dates in this section.
6. **Derive the cronograma** from the effort table — never invent it independently: assign tasks in id order at team capacity (default 8 h/day), business days only (Mon–Fri), starting at the start date; a task longer than a day spans consecutive days; milestones (hitos) close each task group. Total scheduled hours MUST equal the Esfuerzo total.
7. **Author content** for the nine sections per [sections.md](sections.md) — exact titles, exact order, per-section content contracts.
8. **Emit Typst.** Fill the scaffold from [typst-scaffold.md](typst-scaffold.md): write `{output-dir}/main.typ`; if a `.styles/` directory was provided, copy it into `{output-dir}/.styles/` and merge its theme over the defaults (theme changes appearance ONLY — never structure). Without `.styles/`, the document stays brand-neutral.
9. **Compile + verify.** Compile per [typst-scaffold.md](typst-scaffold.md) § Compile (`--ignore-system-fonts`, `--font-path .styles/fonts` when present) — exit 0 AND zero font warnings; render pages to PNG and visually check (diagram legible, icons direct without enclosing boxes, tables not overflowing); run the consistency gates below.
10. **Deliver** `proposal.pdf` (+ `main.typ`, `icons/`).

## Consistency gates (hard — check before delivering)

1. Nine sections, exact canonical titles, exact order — none added, none removed. A section with nothing to say carries a single line `— No aplica: {reason}`, never silently dropped.
2. Every cost row traces to a `meterId` retrieved this session; header carries currency + retrieval date; total re-added by hand.
3. Arquitectura table rows ↔ Costo components ↔ diagram nodes are the same inventory (gate 2 of step 2).
4. Every diagram node for an Azure service has its verified icon, embedded directly (no enclosing shapes — `azure-diagrams`).
5. Cronograma total hours == Esfuerzo total hours; all dates are business days ≥ start date.
6. Style guide affected colors/fonts/logo/footer only — diff against defaults must show zero structural change.
7. Output layout exactly: `{output-dir}/main.typ`, `{output-dir}/.assets/icons/*.svg`, `{output-dir}/proposal.pdf` (+ `{output-dir}/.styles/` when provided). Downloaded resources only under `.assets/`; branding only under `.styles/`.
8. **Whitelabel:** zero brand or tooling references in the deliverable beyond what `.styles/` supplies — grep the document for ByteAid / `assets.byteaid.io` / tool names; any hit without a matching `.styles/` brand is a defect. Microsoft data-provenance citations (e.g. Azure Retail Prices API) are allowed.
9. **Portable fonts:** compile uses `--ignore-system-fonts`; every non-bundled font ships as files in `.styles/fonts/`; zero `unknown font family` warnings.

## Output language

Default Spanish with the canonical section titles in [sections.md](sections.md). If the user requests another language, translate titles 1:1 — same order, same anatomy, same gates.

## Dispatch

| Need | Read |
|---|---|
| The nine section contracts (canonical titles + per-section content rules) | [sections.md](sections.md) |
| Typst scaffold, theme/style-guide contract, compile + visual verification | [typst-scaffold.md](typst-scaffold.md) |
| Pricing mechanics (filters, pagination, pitfalls, quote table) | `azure-pricing-api` |
| Icon resolution + verification + download | `byteaid-assets-icons` |
| Architecture-diagram authoring (Typst fletcher + mermaid, icon-is-the-node) | `azure-diagrams` |
