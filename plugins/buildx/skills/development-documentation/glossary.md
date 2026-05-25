# GLOSSARY — `docs/GLOSSARY.md`

## Purpose

A single, alphabetised dictionary of every domain term, acronym, role name, and externally-visible identifier the project uses. The glossary is the canonical resolver — when a feature, flow, or solution doc says "Operator", "Tenant", "Cancellation Window", "PSP", any reader (human or agent) can resolve the meaning to exactly one entry in this file.

It is read by: the analyst (to keep `FR-NNN` / flow / feature wording consistent), the architect (to align infrastructure / data names with the product vocabulary), the developer (to choose identifiers that match), the test-designer (to assert against the right business terms), the reviewer (to flag drift when production code introduces new vocabulary the glossary does not know).

## Owner

The **analyst** role produces and maintains `docs/GLOSSARY.md`. Every other agent reads it. If a developer or reviewer notices a term in code that has no glossary entry, the gap is surfaced to the orchestrator; the analyst writes the entry — never the consumer.

## Location

`docs/GLOSSARY.md` — UPPERCASE singular, at the repo root's `docs/` folder. Git-tracked. One file per project.

## Shape

```markdown
# Glossary

> Vocabulary of {Product}. Every term used in `docs/`, source code identifiers, UI copy, and operator runbooks resolves to one entry below.

## A

### Account
A billable customer record. Has exactly one `Owner` (a `User`) and zero or more `Members`. An Account is not the same as a `Tenant` — see `Tenant`.

**Related:** `Owner`, `Member`, `Tenant`.
**Code identifier:** `Account` (entity), `AccountId` (Guid v7).
**Appears in:** `FR-003`, `FT-002`, `FL-005`.

### Active Order
An order whose `Status` is one of `Pending`, `Confirmed`, `Shipped`. Cancelled / Delivered / Refunded orders are NOT active.

**Related:** `Order`, `OrderStatus`.
**Code identifier:** filter `Order.IsActive` (computed).
**Appears in:** `FR-014`, `FL-022`.

## B

### Billing Cycle
The 30-day period over which usage is metered and aggregated. Starts on Account creation date; renews every 30 days.

**Related:** `Account`, `Usage`, `Invoice`.
**Appears in:** `NFR-007`, `FT-009`.

...
```

## Entry fields

Each term gets these fields:

| Field | Required | Notes |
|---|---|---|
| Heading `### {Term}` | yes | The term itself, Title-Cased. Use the singular form. |
| Definition | yes | One-to-three sentences. State what it IS and, where useful, what it is NOT. |
| **Related** | yes if applicable | Bullet of other glossary terms it interacts with. Each is a backtick reference to its own heading. |
| **Code identifier** | yes if a typed entity / value object / enum exists | The C# (or stack-equivalent) type or enum name that represents the term in production code. Helps the reviewer flag drift. |
| **Appears in** | yes | Comma-separated `FR-NNN` / `NFR-NNN` / `FT-NNN` / `FL-NNN` references where the term is used. Makes the term greppable both ways. |
| Synonyms | only if any | If the legacy codebase or external vendors use a different word for the same concept, list it here AND add a one-line redirect entry at the synonym's alphabetic position pointing at the canonical term. |
| Banned alternatives | only if drift is a known risk | List wording NOT to use (e.g., "do not say 'Client' — use `Account`"). |

## Rules

- **Alphabetised by heading.** Sections `## A` … `## Z`. Empty sections are omitted, not stubbed.
- **Singular form** for entity names (`Order`, not `Orders`).
- **One term, one entry.** If two definitions are needed, the term is ambiguous — pick one canonical meaning and rename the other concept.
- **Synonym redirects.** If "Client" and "Customer" both appear in the codebase but mean `Account`, both get a one-line entry at their position pointing to `Account`. The original entry under `Account` lists them under **Synonyms**.
- **Code identifiers are kept current.** If a type rename lands in the codebase, the analyst updates the glossary entry in the same slice.
- **No invented terms.** Only terms that actually appear in `docs/`, code, or UI are entered. Terms that "might come up" wait until they do.
- **No history.** As with every other state doc: when a term is renamed, the old entry is removed and the new one takes its place. The motivation lives in the commit message.

## Bootstrap

- **Variant a / b / c1 / existing-code-greenfield-docs**: the analyst seeds `docs/GLOSSARY.md` with every term that appears in REQUIREMENT.md, every feature title, every flow title. From there, the file grows organically as new features land.
- **Variant c2 / c3**: skip (those variants do not touch `docs/`).
- **Migration from legacy monolithic docs**: the analyst extracts terms from the legacy `REQUIREMENT.md` / `FLOWS.md` and writes them as `docs/GLOSSARY.md`. No `(superseded …)` notes.

## Audit checklist (run on every save)

- [ ] Every term has Definition + Appears in.
- [ ] Every cited `FR-NNN` / `FT-NNN` / `FL-NNN` resolves.
- [ ] Every cited code identifier exists somewhere under `src/` (analyst can grep — this is one of the rare cases an analyst is authorised to glance at source to verify a citation; analyst does not change code).
- [ ] No section titled "History", "Deprecated", "Old terms".
- [ ] Sections are in A-Z order; no out-of-place headings.

## See also

- [requirement.md](requirement.md) — the doc whose FRs/NFRs cite glossary terms.
- [data-model.md](data-model.md) — the structural view of how glossary entities connect.
- [skill.md](skill.md) — master table of canonical docs and owners.
