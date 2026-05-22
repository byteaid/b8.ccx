# Forbidden — Editing test files outside the testing scope (moved)

The full ban (no `dotnet new mstest` / `xunit` / `nunit` for non-testing scopes, no editing tests under `[Company].[Product].Test/` from a non-developer surface, route revisions back to the test author, dev re-runs implementation against the revised spec) lives in **`dotnet-testing`** § forbidden-patterns § 5.

Load `dotnet-testing` and read `forbidden-patterns.md`.

Reading test files for context is encouraged. Editing them outside the testing scope is the line.
