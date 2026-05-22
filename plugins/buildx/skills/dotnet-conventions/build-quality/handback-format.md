# Handback format

## Rule

Every delivery from a worker agent follows the canonical handback shape. Five bullets, terse, no narrative filler. The orchestrator reads dozens of these per iteration; consistency is the point.

## The five bullets

```markdown
- **What I did:** {1–3 lines — file paths and a one-line description per edit.}
- **Build status:** dotnet build → 0 errors, 0 warnings (project / solution as relevant)
- **Eradicated in pass (clean-as-you-touch):**
  - {file:line — pattern → replacement} (or "none")
- **Out-of-scope debt found:**
  - {file — N remaining patterns of type X — recommend dedicated cleanup task} (or "none")
- **Concerns / questions:** {ambiguities, contradictions, blockers, missing info — or "none"}
```

## Worked example

```markdown
- **What I did:**
  - `src/Acme.Foo/Acme.Foo.WebAPI/Controllers/OrdersController.cs` — added `Cancel(Guid id)` action returning `Result`.
  - `src/Acme.Foo/Acme.Foo/Services/OrderService.cs` — implemented `Cancel` with idempotency guard.
  - `src/Acme.Foo/Acme.Foo.WebAPI/Program.cs` — registered new `IOrderMapper`.
- **Build status:** dotnet build Acme.Foo.slnx → 0 errors, 0 warnings.
- **Eradicated in pass (clean-as-you-touch):**
  - `OrdersController.cs:14` lowercased route `api/Orders` → `api/orders`.
  - `Program.cs:42-51` removed `#pragma warning disable CS8600`; fixed nullable flow at the source.
- **Out-of-scope debt found:**
  - `Acme.Foo.WebAPI/Program.cs` has 3 `AutoMapper` usages — recommend a hand-written `IXxxMapper` migration task.
  - `Acme.Foo/Services/PaymentService.cs` has `DateTime.UtcNow` in 2 spots — recommend a TimeProvider sweep.
- **Concerns / questions:** none.
```

## Bullet-by-bullet rules

### What I did

- File paths first, description second.
- One bullet per file when there are several files; one bullet total when one file.
- No "I implemented the following changes:" — just the changes.

### Build status

- Always cite the explicit project or solution scope (`Acme.Foo.slnx` or `src/Acme.Foo/Acme.Foo.WebAPI/Acme.Foo.WebAPI.csproj`).
- The line `0 errors, 0 warnings` is **literal** — that's what the orchestrator greps for.
- If the build did not pass, do **not** report success. The handback is honest: "build failed: 1 error in OrderService.cs:42 — investigating" is better than a fabricated success.

### Eradicated in pass

- Each bullet: file, line range, pattern, replacement.
- Only patterns from [../forbidden-patterns/index.md](../forbidden-patterns/index.md) — see [clean-as-you-touch.md](clean-as-you-touch.md) for the policy.
- "none" is a valid answer.

### Out-of-scope debt

- Patterns you saw in the file but did NOT fix because the cascade was too big or the file was outside your scope.
- Concrete: file, count, type, recommended follow-up.
- "none" is a valid answer.

### Concerns / questions

- Genuine ambiguities, contradictions between docs, missing info, blockers.
- One line per concern.
- "none" is a valid answer — it means the work was clean and clear.

## Operational mode

In `OPERATIONAL MODE`, the handback collapses to:

- **What I did:** {paths + one-line per edit}
- **Build status:** dotnet build → 0 errors, 0 warnings
- **Pre-existing warnings I did NOT fix:** {list, or "none"}

No CHANGELOG line, no clean-as-you-touch eradication block, no out-of-scope debt section.

## Enforcement

- **Handback contract:** every standard-mode delivery uses this shape exactly.
- **Orchestrator parsing:** the orchestrator pattern-matches the bullets and routes follow-ups (e.g., out-of-scope debt becomes a backlog candidate).
- **Code review:** if a PR description doesn't follow the shape, surface the inconsistency — consistency across agents is the productivity win.

## See also

- [zero-warnings-rule.md](zero-warnings-rule.md)
- [clean-as-you-touch.md](clean-as-you-touch.md)
- `development-documentation` § changelog — CHANGELOG discipline
- [three-attempts-then-search.md](three-attempts-then-search.md)
