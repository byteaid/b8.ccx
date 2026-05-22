---
name: playwright-dotnet
description: Playwright .NET (1.51.x) reference for E2E/UI testing on .NET 8/9/10 with MSTest. Covers Microsoft.Playwright.MSTest base classes (PageTest, ContextTest, BrowserTest), GetByRole/GetByLabel/GetByTestId locator priority, Expect API auto-retry assertions, RouteAsync network interception, StorageState login-once, traces/videos/PWDEBUG/codegen, Blazor WASM bootstrap and re-render patterns, GitHub Actions / Azure Pipelines / Docker (mcr.microsoft.com/playwright/dotnet), and a 15-problem troubleshooting catalog.
when_to_use: |
  - Trigger keywords: Playwright, Microsoft.Playwright.MSTest, PageTest, ContextTest, BrowserTest, GetByRole, GetByLabel, GetByTestId, Expect API, RouteAsync, StorageState, .runsettings, mcr.microsoft.com/playwright/dotnet, PWDEBUG, codegen, Page.PauseAsync, Blazor WASM E2E, blazor-ready, AriaSnapshot, FrameLocator.
  - Task shapes: scaffold a Playwright + MSTest project, write a UI test for a Blazor page, intercept or mock network requests, log in once and reuse storage state across tests, configure tracing-on-failure and video, set up Playwright in GitHub Actions / Azure Pipelines / Docker, debug a flaky locator, choose between role/label/test-id selectors, port a flaky `WaitForTimeoutAsync` test to auto-retry assertions.
allowed-tools: Bash, Edit, Glob, Grep, Monitor, NotebookEdit, PowerShell, Read, Write
user-invocable: false
paths: ["**/*.runsettings", "**/playwright.config.*", "**/PageTest.cs", "**/PlaywrightFixture.cs"]
---

# Playwright .NET

Reference for Playwright 1.51.x on .NET 8/9/10. Verify your installed version with `dotnet list package | findstr Playwright`. Canonical docs: `playwright.dev/dotnet/`. Stack: MSTest + `Microsoft.Playwright.MSTest` + (optionally) Aspire `DistributedApplicationTestingBuilder`.

## Mental model

- `Microsoft.Playwright.MSTest` is the **only** integration package — never mix `Microsoft.Playwright.NUnit`.
- Three base classes: `PageTest` (default — fresh `Page` per test, shared `Browser` per class), `ContextTest` (multi-page same context), `BrowserTest` (multi-context, e.g. admin+user).
- Locators are first-class: chainable, filterable, auto-retrying. CSS / XPath are escape hatches.
- Every locator action **auto-waits** for actionability (visible, stable, enabled, editable, receives events). You never need `WaitForSelectorAsync` or `Task.Delay`.
- `Expect(...)` assertions auto-retry until the timeout (5s default). `IsVisibleAsync()` is a snapshot — never use it as an assertion.
- `Context` is the unit of isolation: cookies, localStorage, permissions, geolocation, viewport. `StorageState` serializes a context's auth to JSON for reuse.
- `RouteAsync` intercepts at the network layer; `RouteFromHARAsync` records/replays HAR files; prefer Aspire stubs over routes when state is involved.
- `Tracing` produces a `trace.zip` viewable in `playwright show-trace` — capture on-failure only.
- The endpoint URL **must** come from Aspire's `app.GetEndpoint("web")` — never hardcoded.

## Non-negotiable rules

1. **Locator priority:** `GetByRole` → `GetByLabel` → `GetByPlaceholder` → `GetByText` → `GetByAltText` → `GetByTitle` → `GetByTestId` → `Locator(css)` → `Locator(xpath)`. Drop down only when the previous level is non-viable. See [locators](locators.md).
2. **`Expect` always; `IsVisibleAsync` never as an assertion.** `IsVisibleAsync` is a snapshot. Use it only as an `if` predicate. See [assertions](assertions.md).
3. **No `Thread.Sleep`, no `WaitForTimeoutAsync`.** If you reach for them, your locator or assertion is wrong. The single documented exception is `<Virtualize>` scrolling (see [blazor-wasm](blazor-wasm.md)).
4. **`data-testid` in kebab-case** (`order-summary`, not `OrderSummary`). Used only when no semantic role exists.
5. **Edge channel (`channel: "msedge"`) by default** in `.runsettings`. Bundled Chromium acceptable in CI; install MSEdge in CI when needed.
6. **Headless in CI; headed only via `PWDEBUG=1` locally.** Never check in `Headless=false`.
7. **Tracing on-failure** in `[TestCleanup]` via `TestContext.CurrentTestOutcome`. Always `Screenshots=true, Snapshots=true, Sources=true`.
8. **`StorageState` for login-once.** Run login in `[AssemblyInitialize]`, save to JSON, set `StorageStatePath` in `ContextOptions()` per test class. Never log in inside individual tests.
9. **`Microsoft.Playwright.MSTest` and `Microsoft.Playwright` versions must match.** Don't pin both — let `MSTest` pull the transitive.
10. **`BaseURL` from Aspire**, not hardcoded `localhost:5000`. See `dotnet-aspire` § playwright-testing.
11. **Wait for response BEFORE the action that triggers it.** Use `RunAndWaitForResponseAsync` to make the order impossible to invert.
12. **Strict-mode violations are bugs in the locator**, not in Playwright. Add a `Filter` or scope to a parent — don't blanket-add `.First`.

## Sub-file map

| File | When to read |
|---|---|
| [setup-mstest](setup-mstest.md) | Initial project setup, NuGet, browser install, full `.runsettings`, env vars, `PageTest`/`ContextTest`/`BrowserTest`, Aspire boundary |
| [locators](locators.md) | Choosing a locator, official priority, `GetByRole`/`GetByLabel`/`GetByTestId` reference, chaining, `Filter`, `FrameLocator`, shadow DOM |
| [actions-waiting](actions-waiting.md) | `ClickAsync`, `FillAsync`, `SelectOptionAsync`, keyboard, drag, actionability table, when explicit waits are/aren't needed, timeouts |
| [assertions](assertions.md) | `Expect` API: visibility, state, text, count, attributes, ARIA snapshot, soft assertions, negation, assertion-as-wait |
| [tracing-debugging](tracing-debugging.md) | Tracing on-failure, video, screenshots, `PWDEBUG=1`, Inspector, `Page.PauseAsync`, codegen, VS / VS Code debug |
| [network-interception](network-interception.md) | `RouteAsync` mock/abort/modify, HAR record/replay, WebSocket interception, block analytics, `RunAndWaitForResponseAsync` |
| [auth-storage](auth-storage.md) | `StorageState` login-once, multi-role admin+user, cookie vs bearer, Aspire integration |
| [blazor-wasm](blazor-wasm.md) | Bootstrap waiting (`blazor-ready`), `data-testid` placement, `EditForm`+validation, `Virtualize`, client-side routing, SignalR, re-render races |
| [ci-cd](ci-cd.md) | GitHub Actions, Azure Pipelines, browser caching, Docker `mcr.microsoft.com/playwright/dotnet`, artifact upload |
| [troubleshooting](troubleshooting.md) | 15 common failures in `Symptom → Cause → Fix` form |
| [advanced](advanced.md) | Page Object Model, parallel execution, device/locale emulation, file upload/download, accessibility (axe-core, ARIA), iframes, `APIRequestContext` |

## Quick decision matrix

| Symptom / Need | Read |
|---|---|
| "Which locator should I use?" | [locators](locators.md) |
| "My test is flaky / has `Task.Delay`" | [actions-waiting](actions-waiting.md), [assertions](assertions.md) |
| "Element is not stable / re-render race" | [blazor-wasm](blazor-wasm.md), [troubleshooting](troubleshooting.md) §6 |
| "How do I log in once?" | [auth-storage](auth-storage.md) |
| "Mock /api/orders" | [network-interception](network-interception.md) |
| "Trace on failure" | [tracing-debugging](tracing-debugging.md) |
| "Pipeline times out / black screenshots" | [ci-cd](ci-cd.md), [troubleshooting](troubleshooting.md) §1, §7 |
| "Blazor doesn't navigate / NavLink hangs" | [blazor-wasm](blazor-wasm.md), [troubleshooting](troubleshooting.md) §9 |
| "Multi-role test" | [auth-storage](auth-storage.md), [setup-mstest](setup-mstest.md) (`BrowserTest`) |

## Cross-skill references

- `dotnet-aspire` § playwright-testing — Aspire `[AssemblyInitialize]` fixture, `app.GetEndpoint("web")`, `WaitForResourceHealthyAsync`, `ContextOptions().BaseURL` override.
- `dotnet-aspire` § integration-testing — pure HTTP testing without a browser.
- `dotnet-blazor-auth` (future) — cookie+BFF auth flows tested via `StorageState`.

## Upstream sources

- https://playwright.dev/dotnet/docs/intro
- https://playwright.dev/dotnet/docs/api/class-playwright
- https://playwright.dev/dotnet/docs/best-practices
- https://playwright.dev/dotnet/docs/locators
- https://playwright.dev/dotnet/docs/test-runners
- https://www.nuget.org/packages/Microsoft.Playwright.MSTest
