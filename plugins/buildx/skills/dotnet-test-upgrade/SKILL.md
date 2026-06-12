---
name: dotnet-test-upgrade
description: Slash-command procedure that upgrades an EXISTING test project to the latest `dotnet-testing` canon — audit (Level 0/1/2 sweeps) → reconcile docs first (flows own the tests) → migrate shape (singular `[Company].[Product].Test` under `test/`, MSTest 4.x, single `AppHostFixture`, `Blaztrap.Aspire.FileLogging`) → purge garbage (unit/guard tests, hand-rolled fakes, dead helpers, in-repo artefact folders, stale logs, path env vars, `[TestCategory]` tiers) → re-orchestrate escaped dependencies (emulator / stub / DNS-level wiring — one suite, zero secrets) → verify GREEN. Follows the documentation > tests pipeline strictly — no test is renamed, moved, or deleted before its flow mapping is resolved.
when_to_use: |
  - User-invocable only (`/dotnet-test-upgrade`): a destructive, suite-wide migration the user must trigger deliberately — never auto-invoked by the model.
  - Invoke when: the test suite predates the current `dotnet-testing` canon; plural `.Tests` project name or wrong location; ad-hoc per-class mounts or a legacy shared `[AssemblyInitialize]` fixture; `Blaztrap.Aspire.Testing.FileLogging` / `AddResourceFileLogging` still referenced; logs and recordings dispersed in-repo (`tests/automated/`, repo-root session files, `BLAZTRAP_TEST_RUN_DIR`-style env vars); unit/guard/parity tests or hand-rolled fakes accumulated; orphan tests with no `FL-NNN` mapping.
user-invocable: true
disable-model-invocation: true
allowed-tools: Bash, Edit, Glob, Grep, PowerShell, Read, Write
---

# /dotnet-test-upgrade — conform an existing test suite to the current canon

Deterministic migration procedure. The authoritative rules live in `dotnet-testing` (layout, mstest-integration, forbidden-patterns, seeding) — this skill only sequences the work and the gates. Read the relevant `dotnet-testing` sections before each phase; do not re-derive rules from memory.

**Pipeline invariant: documentation > tests.** Tests realise flows; flows live in `docs/features/FT-*/flows/FL-*-*.md`. Every survive/rename/delete decision is resolved against the flow map BEFORE any test file is touched. A test edit that leaves a stale `## Test` FQN behind is a defect of this procedure.

**Dispatch note.** When the `Agent` tool is available (running under `buildx` as the session agent), route phase 1 doc work through `analyst`, test authoring through `dotnet-test-designer`, build/test runs through `dotnet-developer`, and the closing pass through `dotnet-reviewer`. When it is not (plain session), execute inline following the skills — same steps, same gates.

## Phase 0 — Audit (read-only; produces the migration table)

1. Run the **Level 0/1/2 sweeps** from `dotnet-testing` § layout § Enforcement and the `dotnet-test-designer` sweep definitions:
   - Project name/location (plural `.Tests`, `tests/` root, second test project).
   - Every `DistributedApplicationTestingBuilder` call site (anything outside `AppHostFixture.cs`, legacy shared fixtures, `[AssemblyInitialize]` hosting the SUT).
   - Logging package (`Blaztrap.Aspire.Testing.FileLogging` / `AddResourceFileLogging`).
   - Artefact paths (in-repo folders such as `tests/automated/`, repo-root session files, `BLAZTRAP_TEST_RUN_DIR` / `*_LOG_DIR` env vars).
   - Surface folders vs the executables the AppHost declares (cross-check `docs/SOLUTION.md`); flag `Service/`, `Hosting/`, `Authorization/`-style folders.
   - Hand-rolled fakes (test classes implementing production ports, in-process `HttpListener` / inline web-server sinks).
   - Escaped dependencies (tests hardwired to services the AppHost does not orchestrate: real ARM / Graph / mail / partner APIs; real email from the default run is a blocker) and any `[TestCategory]` tier (`RealInfra`, `Slow`, `Nightly`) — one suite, one code path.
   - Scattered run wiring: `Environment.GetEnvironmentVariable` calls outside a single `TestSettings.cs`, ad-hoc behaviour knobs (headed flags, slow-mo, timeout env vars), topology- or knob-conditionals inside test methods (code forks).
   - Operator-in-the-loop auth (headed manual sign-in instead of unattended `TestResults/.auth/{role}-state.json`).
   - MSTest version + parallelism pin; banned packages.
   - AppHost: `WithDataVolume` / `ContainerLifetime.Persistent` / data bind mounts (report only — the AppHost is a separate authorisation, see Phase 5).
2. Build the **flow reverse-map**: every `[TestMethod]` ↔ `## Test` FQN in `docs/features/FT-*/flows/FL-*-*.md`. Classify each method: `mapped` / `orphan` / `banned-shape` (guard, fake-dependent, operator-dependent).
3. Detect **garbage**: helpers no longer referenced once banned shapes go, committed log/recording files, stale artefact directories, retired stubs, `.gitignore` gaps.
4. Emit the **migration table** — one row per finding: `# | Finding | Where | Resolution (fix / migrate / delete / create-flow / accept-as-debt) | Phase`. Present it and **STOP for user approval**. Deletions are never silent; the user resolves every `delete` and `create-flow` row before Phase 1 starts.

## Phase 1 — Documentation first

1. For every surviving test whose flow is missing → create/extend the flow (via `analyst` when dispatching; otherwise per `development-documentation` § flow), or downgrade the test to a `delete` row if the user declines the flow.
2. For every banned-shape test whose behaviour matters → record the gap: the real-surface flow that should cover it (new/extended `FL-NNN`) or a `BG-NNN`/`BL-NNN` row. A misleading green check is worse than a visible gap.
3. Compute the **post-migration FQN** for every surviving test (new project name, new namespace, new surface folder, `{Area}_Tests.cs` class, `{Action}_{Scenario}_{Expectation}` method) and update each flow's `## Test` block to the new FQN in the same change-set that renames the test (Phase 3) — never let docs and code diverge between commits.
4. Purge doc debris while there: `DT-NNN` / `D-NNN` references inside flow files (debt lives out-of-repo; Decisions are retired) — move the useful content into the flow's normal sections or drop it.

## Phase 2 — Project shape

1. Rename/move to the canon: `test/[Company].[Product]/[Company].[Product].Test/` (singular). Update `.slnx`, project references, namespaces.
2. `.csproj` per `dotnet-testing` § layout: unified `MSTest` 4.x, `Aspire.Hosting.Testing`, `Blaztrap.Aspire.FileLogging` (remove the legacy `Blaztrap.Aspire.Testing.FileLogging`), remove every banned package.
3. `AssemblyInfo.cs`: `[assembly: Parallelize(Workers = 1, Scope = ExecutionScope.MethodLevel)]` (replace `DoNotParallelize` / `.runsettings` equivalents; keep `.runsettings` only if it carries non-parallelism config).
4. Create the surface folders derived from the AppHost's executables; plan the file moves (executed with the class migrations in Phase 3).

## Phase 3 — Fixture consolidation

1. Create the canonical `AppHostFixture.cs` + `TestArtifacts.cs` + `TestSettings.cs` from `dotnet-testing` § mstest-integration (per-derived-class mount, per-class `logs/{ClassName}/`, `AddFileLogging`, `CreateAsync(TestSettings.AppHostArgs())`). Consolidate every scattered env-var read and behaviour knob (headed, slow-mo, timeouts, topology flags) into `TestSettings` with `TESTRUN_*` names and zero-setup defaults; after this phase `TestSettings.cs` is the only `GetEnvironmentVariable` call site — grep to confirm.
2. Migrate every surviving class: inherit `AppHostFixture`, strip its inline mount/dispose, keep only surface plumbing in a plain `[ClassInitialize]` / `[ClassCleanup]`. After this phase `AppHostFixture.cs` is the only `DistributedApplicationTestingBuilder` call site — grep to confirm.
3. Re-route every artefact write through `TestArtifacts.RunDir(context)` / `AuthDir(context)`: logs, traces, screenshots, recorded payloads, auth state. Remove env-var fallbacks.
4. Replace operator-in-the-loop sign-in with unattended auth-state generation (`[AssemblyInitialize]` auxiliary pass per `playwright-dotnet` § auth-storage) writing `TestResults/.auth/{role}-state.json`.

## Phase 4 — Purge garbage

1. Delete the resolved `delete` rows: guard/unit/parity classes, fake-dependent tests, hand-rolled fakes and in-process sinks (replaced by stub projects / real seeding where a survivor needs them), legacy fixture bases, now-unreferenced `Infrastructure/` helpers.
2. Delete in-repo artefact debris from disk AND git: `tests/automated/`, repo-root session files, committed `*.log` / recordings. Add `TestResults/` to `.gitignore` if missing.
3. Delete empty folders and folders outside the derived surface set.
4. Sweep for dead code the deletions exposed (unused usings, orphaned `TestData/` files, seed helpers with no caller).

## Phase 5 — Re-orchestrate escaped dependencies (one suite, one code path)

1. For every test hardwired to a service the AppHost does not orchestrate, fix the **topology, never the taxonomy**: wire an emulator where one exists; ship a stub project for vendors without one; use DNS-level wiring (hosts-entry / container-DNS resolution of the pinned hostname to the stub, dev-cert TLS) when the SDK does not expose a base-URL override. See `dotnet-testing` § mstest-integration § One suite, one topology.
2. Remove every `[TestCategory]` tier (`RealInfra`, `Slow`, `Nightly`) and every credential the **default run** needed — after this phase the whole suite passes on any machine with zero env vars set. Real email from the default run is a blocker until its channel is stubbed.
3. Preserve the real-infrastructure capability as **wiring, not code**: topology switching lives solely in `TestSettings.AppHostArgs()` (`TESTRUN_REAL_INFRA` + `TESTRUN_CS_*` secrets from env). Delete any topology-conditional that survived inside a test method or production code — same classes, same assertions, both runs.
4. AppHost changes this phase requires (new stub resources, emulator wiring, DNS entries) and AppHost findings from the audit (persistent lifetimes, data volumes) are **surfaced for authorisation, not silently applied** — the AppHost belongs to the developer slice; hand the list to the orchestrator/user as follow-up `BL-NNN` rows or get explicit approval before touching it.

## Phase 6 — Verify and hand off

1. `dotnet build` clean; `dotnet test` GREEN — the whole suite, no filter, no secrets (via `dotnet-developer` when dispatching).
2. Confirm: every flow `## Test` FQN resolves to exactly one method; zero enforcement-grep hits (re-run Phase 0 sweeps — they must come back clean); all artefacts of the verification run landed under `TestResults/{run-id}/...` including `apphost.log` per class.
3. Hand off: migration table with final per-row status, files added/renamed/deleted, flows touched, `BG-NNN`/`BL-NNN` follow-ups opened (coverage gaps, AppHost lifetimes), test-run summary. Recommend a closing `dotnet-reviewer` pass (testing-conformance slugs) before committing.

## Constraints

- **No production code edits** except deleting test-only adaptations the audit found (and only with the user's approval on that row); anything larger is a follow-up slice.
- **Docs and tests move together**: every rename lands with its `## Test` FQN update in the same change-set.
- **Never silently delete** — every deletion traces to an approved migration-table row.
- **Stop on ambiguity**: a test that fits no flow and no garbage category goes back to the user, not into a guess.

## Cross-references

- `dotnet-testing` — the canon this skill conforms suites to (layout § Enforcement, mstest-integration, forbidden-patterns, seeding).
- `development-documentation` § flow — the `## Test` block contract.
- `playwright-dotnet` § auth-storage — unattended auth-state generation.
- Sibling agents (when dispatching): `analyst`, `dotnet-test-designer`, `dotnet-developer`, `dotnet-reviewer`.
