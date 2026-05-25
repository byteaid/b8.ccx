---
name: development-methodology
description: The team's canonical development methodology is "test-first orchestrated" — the test-designer writes the failing real test BEFORE the developer touches production code; the developer implements until the test passes; the developer runs `dotnet test` to verify. There is no TDD/direct selection: the cycle is fixed and the orchestrator enforces it. The methodology is language-agnostic in shape (the same cycle applies to any stack with a real test surface) but stack-specific in tooling (per-stack test-designer + per-stack developer skills + per-stack test runner). Real tests only (HTTP / UI / gRPC / CLI / queue); no unit tests, no mocks, no test-only adaptations in production code.
when_to_use: |
  - Trigger keywords: methodology, test-first, real test, failing test, "make the test pass", orchestration cycle, who writes the test, can I add a mock, can I add an env-test branch, /__test__ endpoint.
  - Task shapes: starting any non-trivial coding task and needing to confirm the order; auditing whether an in-flight change followed the cycle; deciding whether a failing test reveals missing production code or a misframed flow.
allowed-tools: Glob, Grep, Read
user-invocable: false
---

# Development Methodology

L1 leaf. The methodology is fixed: **test-first orchestrated**. The choice is not yours; the orchestrator enforces it.

## The cycle

```
1. analyst confirms desired state (REQUIREMENT.md, features/**, including the affected FL-NNN)
2. dotnet-test-designer writes the failing real test for each affected FL-NNN
   - FQN recorded in the flow file's `## Test` block
3. dotnet-architect plans the implementation (todo.md, T-NNN blocks of 10)
4. dotnet-developer implements the production code
   - reads the test as the contract
   - writes the minimum production code to satisfy the test
   - does NOT touch the test
   - does NOT add test-only adaptations to production
   - applies the canonical rules at write-time (try/catch must do work; DI motor only;
     search-before-create; hexagonal invariants; English only incl. enums/constants/routes;
     no secrets in VCS; no magic literals — constants + enums; exceptions logged, never leaked [P0])
5. dotnet-developer runs `dotnet test --filter "FullyQualifiedName~{FQN}"`, then full `dotnet test`
6. dotnet-reviewer second-pass review — registers carried rule violations to debt.md (DT-NNN rows),
   discriminates slice-scope `active` from project-level `structural`. Blocker rows are cleared
   by re-dispatching dotnet-developer (or dotnet-test-designer if a test must change) BEFORE closure.
7. orchestrator closes the slice (delete the backlog/bugs row, delete any cleared debt rows, commit)
```

The cycle is enforced by `buildx`. Subagents do not pick the cycle, do not switch its order, and do not skip steps.

## Hard rules

1. **Tests are written by the test-designer, never by the developer.** The developer reads tests; they do not author them.
2. **Real tests only.** The test exercises the same code that runs in production. Options: HTTP through the Aspire AppHost, Playwright against the running UI, real gRPC client, real CLI invocation, real queue / event triggers.
3. **No unit tests.** A behaviour that "can only be tested as a unit" is mis-modelled — escalate to `analyst` to revisit the flow.
4. **No mocks, no fakes, no in-memory substitutions of critical infrastructure.** `Moq`, `NSubstitute`, `FakeItEasy`, `WireMock.Net` are banned in test project references.
5. **No test-only adaptations in production code.** No `if (env.IsTest)`, no `/__test__/...` endpoints, no `SeedForTest()` methods, no `services.AddSingleton<I…, Fake…>()` guarded by environment, no in-memory replacements of production infrastructure. **The same code that runs in production is the code under test.**
6. **RED before GREEN.** The test added by the test-designer fails today on the assertion, not on a compile error or missing project. If the test passes immediately, it was not exercising the new behaviour — the test-designer rewrites it.
7. **One slice = one block of GREEN.** Bundling three features behind one test invalidates the cycle. One flow → one test → one or more developer tasks → GREEN.
8. **Three-attempts-then-search.** If three RED→fix attempts fail to produce a clean GREEN, STOP and re-read the spec / search official docs. See `dotnet-conventions` § three-attempts-then-search.
9. **No backfilled tests.** Writing the production code first and "adding a test that proves it works" is not the cycle. If the developer realises they implemented something the test does not cover, the test-designer is dispatched to extend the test BEFORE the developer continues.

## When the cycle does not apply

The cycle is the law in projects that have a real test surface (typically: an Aspire-orchestrated topology with a `Company.Product.Test` project). It does not apply when:

- The project is in bootstrap variant **c2** or **c3** (no docs, no test surface). The developer makes the change directly; manual verification is mandatory; the gap is surfaced to the user as a follow-up to set up the test surface.
- The task is a single-`.cs` file-based script with no test project (see `dotnet-file-based-apps`). The script's behaviour is verified by execution; if the user wants automated coverage, the script graduates to a project first.
- The task is purely mechanical and does not change observable behaviour (rename, dead-code removal, formatting, dependency-version bump). The developer applies the change directly; full `dotnet build` and `dotnet test` confirm no regressions.

In every other case, the cycle applies. **There is no "I'll just add the test later" option.**

## Anti-patterns (refuse and escalate)

- "Just write the production code first; we'll add the test once it works." — refuse. Dispatch test-designer first.
- "Add a `SeedForTest()` method so the integration test can prepare the database." — refuse. Production-data preparation goes through the same admin API a real operator uses.
- "Mock `IEmailSender` so the test does not try to send a real email." — refuse. Ship a stub email service project, register it in the AppHost like any other resource.
- "Add `if (Environment.GetEnvironmentVariable("IS_TEST") == "1")` to disable rate limiting in tests." — refuse. Either the test runs under real rate-limiting conditions, or rate-limiting is configurable (and the test sets the config via the same surface the prod ops team uses).
- "Backfill a test after I'm done so it looks like TDD." — refuse. The cycle requires the test to fail first against the prior code.

If a subagent (developer or test-designer) reports they were asked to do any of these, the orchestrator stops and renegotiates the contract.

## Cross-references

- Related skill: `dotnet-testing` — what a "real test surface" looks like in the .NET stack (single `Company.Product.Test`, MSTest, per-class AppHost mount, surface folders, seeding strategies, forbidden patterns).
- Related skill: `dotnet-conventions` § three-attempts-then-search — the bail-out rule that interrupts a stuck RED→GREEN loop.
- Related skill: `dotnet-hexagonal-architecture` — what the architecture-level "real surface" looks like in greenfield .NET.
- Sibling agents: `dotnet-test-designer` (writes tests), `dotnet-developer` (implements + runs tests), `analyst` (owns desired state — REQUIREMENT, GLOSSARY, DATA-MODEL, features), `dotnet-architect` (plans + SOLUTION), `dotnet-reviewer` (second-pass review + debt register), `buildx` (enforces the cycle).
- Repo rules: `AGENTS.md` § Skills.
