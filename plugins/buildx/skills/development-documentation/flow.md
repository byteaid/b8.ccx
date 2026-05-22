# Flow document — `docs/features/FT-NNN-{kebab}/flows/FL-NNN-{kebab}.md`

## Purpose

A flow document answers: *exactly what happens when an actor takes one specific route through a feature?* It is the most granular unit of desired-state documentation — one possible path = one flow = one real test.

It is read by: the test-designer (to write the test that realises this flow), the developer (to know which user-visible behaviour must remain intact), the analyst (to keep the route faithful to the user's intent).

## Owner

- The **analyst** creates the file, writes Trigger / Steps / Postcondition / error notes, and lists the parent feature + linked FRs.
- The **test-designer** owns the `## Test` block — fills in the fully-qualified name (FQN) once the test class/method is created, and updates it on any rename.

## Where

- Path: `docs/features/FT-NNN-{kebab}/flows/FL-NNN-{kebab}.md`, where `FT-NNN` is the parent feature's ID and `FL-NNN` is the flow's globally-unique ID.
- Tracked: yes (git).
- Lifecycle: desired state — **rewritten in place** when the route evolves. No history, no Decisions log.

## Hard rule — 1 flow = 1 route = 1 test

A flow represents exactly one possible path through the system. If the user clicks button B instead of button A, that is a different flow. If the API returns 401 instead of 200, that is a different flow. The premise:

- **One route = one flow file = one real test.** Strict.
- If a route is exercised both via UI and via the underlying API by the same user journey, it is ONE flow and the test is a Playwright end-to-end test that covers the whole path.
- If the API is verified separately (without UI), that is a DIFFERENT flow with its own `FL-NNN` and its own HTTP test.

## What goes in

- Flow title (the route, not the feature).
- Parent feature (`FT-NNN`).
- Linked FRs (and optionally NFRs).
- Trigger: what the actor does that initiates this exact route.
- Steps: numbered, observable from the actor's perspective.
- Postcondition: what is true after the route completes successfully.
- Error / alternative notes (optional): brief notes on what the system does NOT do here (the alternatives have their own flow files).
- **`## Test` (mandatory)** — the fully-qualified name of the single real test that realises this flow, plus fixture / data / assertion notes.

## What does NOT go in

- Multiple routes in one file. Branches go in separate `FL-NNN-{kebab}.md` files.
- Implementation details (controllers, endpoints, queries) — those live in [solution.md](solution.md).
- Unit tests, mock-based tests, in-memory replacements — see § Test rules below.

## Format

```markdown
# FL-NNN — {Route title}

**Feature:** FT-NNN ({feature title})
**FR coverage:** FR-003, FR-005

## Trigger

{What the actor does that initiates this exact path. Include the input shape if relevant.}

## Steps

1. {Action 1, observable from the actor's perspective.}
2. {System response 1, observable.}
3. {Action 2 or system response 2.}
4. {...}

## Postcondition

- {What is true in the system after the route completes successfully.}
- {Any side effects (counter incremented, event emitted, etc.).}

## Test

FQN: `Company.Product.Test.{Surface}.{Area}_Tests.{Action}_{Scenario}_{Expectation}`

Fixture: {data the system must hold before the test runs — seeded users, configured tenants, etc.}
Data: {the inputs the test sends.}
Assertions: {what the test checks — status code, body shape, UI element, side-effect row, etc.}
```

### Concrete example

```markdown
# FL-002 — Login con contraseña incorrecta

**Feature:** FT-001 (Login)
**FR coverage:** FR-003, FR-005

## Trigger

User submits the `/login` form with a valid email and an incorrect password.

## Steps

1. Browser POSTs `{email, password}` to `/api/login`.
2. System validates the email exists.
3. System validates the password and finds it does not match.
4. System returns HTTP 401 with body `{"error":"invalid_credentials"}`.
5. UI displays `"Email or password invalid"` (no hint about which one failed).

## Postcondition

- No session is created.
- The user's `failed_attempts` counter is incremented by 1.

## Test

FQN: `Company.Product.Test.Http.Login_Tests.Login_WrongPassword_Returns401`

Fixture: user `bob@x.com` seeded with known password hash, `failed_attempts = 0`.
Data: `{email: "bob@x.com", password: "wrong"}`.
Assertions: status 401, body matches `{"error":"invalid_credentials"}`, post-test `failed_attempts = 1`.
```

## Test rules — non-negotiable

Owned by the per-stack test-designer skill (e.g. `dotnet-test-designer`), but enforced here in the contract of every flow file:

1. **Real tests only.** The test exercises the same code that runs in production. Options: Playwright against the running UI, HTTP against the running app, direct CLI execution, gRPC against the running service, real queue / event triggers.
2. **No unit tests.** A flow that "can only be tested as a unit" is mis-modelled — find the real surface or remove the flow.
3. **No mocks, no fakes, no in-memory substitutions of critical infrastructure.** No `Moq`, `NSubstitute`, `FakeItEasy`, `WireMock.Net`. If a third party has no native emulator, ship a real stub project.
4. **No test-specific code paths in production.** No `if (env.IsTest)`, no `/__test__/seed` endpoints, no `SeedForTest()` methods, no `services.AddSingleton<I…, Fake…>()` guarded by an environment check. The premise: the same code that ships to prod is the code under test.
5. **One flow → one test.** Strict 1:1. If a flow appears to need two tests, it is two flows.

## Lifecycle

- **Created** by the analyst when a new route is identified — picks the next free global `FL-NNN`, places the file under the correct feature folder, writes Trigger / Steps / Postcondition, sets up the `## Test` block with a tentative FQN.
- **`## Test` populated** by the test-designer when the test class/method is authored. The FQN in the doc and the actual code must agree exactly.
- **Updated in place** when the route changes. No history, no Decisions log.
- **Renamed:** `{kebab}` may be renamed when the route description changes; `FL-NNN` stays stable. Update the test FQN if renaming requires it.
- **Deleted** when the route is retired. Delete the file. The corresponding test file should be deleted in the same commit; the test-designer is responsible.

## IDs

- `FL-NNN` — flows; globally unique across the project.
- `FT-NNN` — referenced as the parent feature; defined in the sibling `feature.md`.
- `FR-NNN`, `NFR-NNN` — referenced in "FR coverage"; defined in [requirement.md](requirement.md).

## See also

- [feature.md](feature.md) — the parent feature this flow belongs to.
- [requirement.md](requirement.md) — the FRs this flow covers.
- [id-taxonomy.md](id-taxonomy.md) — the `FL-NNN` numbering rules.
- Per-stack test-designer skill — the role that fills the `## Test` FQN (e.g. agent `dotnet-test-designer` for .NET).
