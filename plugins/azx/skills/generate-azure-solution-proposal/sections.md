# Section Contracts — Azure Solution Proposal

**Version:** v1.0.0
**Updated:** 2026-06-04

L2 leaf. The nine sections, in this exact order, with these exact canonical (Spanish) titles. Each contract states what the section MUST contain, its shape, and what is forbidden. Content language follows the document language; anatomy never changes.

## 1. Resumen ejecutivo

Audience: decision makers who read nothing else. Half a page maximum.

- Shape: one framing paragraph (≤ 3 sentences) + 4–6 bullets.
- MUST state: the problem in one line, the proposed solution in one line, total monthly infrastructure cost (currency + priced date), total effort hours, total calendar duration with end date, and the single most important decision or risk.
- Every figure here MUST be copy-identical to the figure in its source section (cost ↔ § 5, hours ↔ § 6, dates ↔ § 7). Write this section LAST.
- Forbidden: technology detail, meter names, task lists, marketing prose.

## 2. Antecedentes

Context of the problem or need being solved.

- Shape: 2–4 short paragraphs.
- MUST cover: current situation, the pain/need that triggers the project, why now, and any prior attempts or systems being replaced.
- Source: the user-provided problem context (blocking input). Do not invent background facts; gaps are stated as assumptions in § 8, not filled with fiction.

## 3. Propuesta de solución

The proposed solution as a GENERAL description — the resource-by-resource detail lives in § 4 and must not be anticipated here.

- Shape: 2–4 paragraphs of prose. No per-resource bullet lists, no tables.
- MUST cover: the overall approach (what kind of platform, what redundancy/operating model), how it resolves the need stated in § 2, the key technology decisions with their drivers (cost, scale, compliance, time-to-market), and the significant rejected alternatives ("se descartó X por …").
- Azure resources MAY be named inline as part of the narrative — naming is as far as it goes. SKUs/tiers, configurations, regions, and per-resource roles are § 4's content; repeating them here is a defect.
- Anti-redundancy gate: no sentence in § 3 may restate a row of the § 4 table; if removing § 4 would leave a resource's tier/role unknown, § 3 is correct.

## 4. Arquitectura

High-level architecture/infrastructure diagram + resource-role table.

- Shape: (a) the diagram, (b) the table — both mandatory.
- Diagram: Typst `fletcher` per `byteaid-assets-icons` § embedding — every Azure node carries its verified icon directly (no enclosing boxes), groupings (regions/zones) as dashed enclosures, edges labeled protocol/port, neutral caption describing the topology (no tooling or provider credits — whitelabel invariant, [typst-scaffold.md](typst-scaffold.md)).
- Table — one row per resource in the component inventory (same inventory as § 5):

| Columns | Recurso (icon + name) | SKU / tier | Región | Rol en el diseño |
|---|---|---|---|---|

- Gate: diagram nodes, table rows, and § 5 cost components are the same set.

## 5. Costo de infraestructura

Estimated monthly cost breakdown of the Azure deployment.

- Shape: the quote table from `azure-pricing-api` § quoting-recipes verbatim — columns Componente / Meter (`meterId`) / Precio unitario / Unidad / Cantidad mensual est. / Mensual; grouped by region/scope; bold total row.
- Header line carries: currency, retrieval date, regions.
- Below the table: commitment options (labeled, never blended into the base) and the pricing assumptions (730 h/month, quantities, free grants not netted).
- Every price obeys `azure-pricing-api` non-negotiable rules; gates: one currency, one retrieval date, hand-re-added total.

## 6. Esfuerzo

Effort in hours to deliver the proposal, divided into tasks. **No dates here.**

- Shape: table — Id (`T01..Tnn`, zero-padded, dependency order) / Tarea / Descripción (one line) / Horas — plus a bold total row.
- Task granularity: 4–40 h each; bigger means split. Include the non-coding work (provisioning/IaC, environments, testing, documentation, handover/PM) — a proposal with only build tasks is incomplete.
- Hours are integers. Estimation basis (team seniority assumption) goes to § 8.
- Tasks are grouped into named contiguous phases (e.g. Fundación → Cómputo → Datos → Cierre); phases drive the § 7 Gantt task groups and milestone set.

## 7. Cronograma

The effort distributed across N dated days, with milestones.

- DERIVED from § 6 — never authored independently: tasks in id order, team capacity (default 1 person × 8 h/business-day), Mon–Fri only, starting at the user-provided start date; long tasks span consecutive days.
- Shape: (a) a Gantt chart divided by week + (b) a milestones table — both mandatory.
- Gantt (`timeliney`, scaffold in [typst-scaffold.md](typst-scaffold.md)): time axis in weeks — 1 unit = 1 week = 5 business days; two header lines (Semana N / date range Mon–Fri); one `taskgroup` per § 6 phase, in phase order; a task spanning business days `[a..b]` draws from `(a−1)/5` to `b/5`; a milestone on day `d` marks at `d/5`. This mapping is fixed — same schedule, same chart.
- Milestones: kickoff (day 1), end of each § 6 phase, entrega final (last day). Every milestone appears BOTH as a dashed mark on the Gantt and as a row in the table — Hito / Día / Fecha (YYYY-MM-DD) — same set, no extras in either.
- One intro line above the chart states: total hours, capacity, N business days, start and end dates.
- Gates: scheduled hours total == § 6 total; every date a business day; end date repeated in § 1; Gantt marks == table rows.

## 8. Pre requisitos y supuestos

What must exist and/or is assumed before the project starts.

- Shape: two bullet lists under bold leads — **Pre requisitos** (verifiable conditions: subscription + permissions, accesses, named contacts, approved budget) and **Supuestos** (assumptions the estimate depends on: quantities behind § 5, seniority behind § 6, content gaps from § 2).
- Every assumption that scales a number in § 5/§ 6 MUST appear here.

## 9. Fuera de alcance

Points explicitly NOT covered by the plan.

- Shape: bullet list; each line names the excluded item and, when useful, the condition under which it would become a separate engagement ("puede cotizarse por separado").
- Minimum coverage to consider: application code beyond what § 6 lists, data migration, post-delivery operation/support, licensing outside Azure, organizational change management, security certifications.
- Never empty: if genuinely everything is covered, state the boundary explicitly ("el alcance se limita exactamente a las tareas de § 6").
