# Clean-as-you-touch

When you open a file to make a change, you are responsible for its state when you hand it back. Every prohibited pattern found **inside the file you're already editing** must be fixed in the same pass — not deferred to a cleanup ticket.

Contract: *"you touched it, you own it clean"*.

## Eradicate without asking (inside the files you edit)

| Pattern | Action | Reference |
|---|---|---|
| Minimal APIs | Refactor to controller | [../forbidden-patterns/no-minimal-apis.md](../forbidden-patterns/no-minimal-apis.md) |
| Warning suppressions | Remove, fix root cause | [../forbidden-patterns/no-warning-suppression.md](../forbidden-patterns/no-warning-suppression.md) |
| Uppercase route templates | Lowercase them + confirm `RouteOptions.LowercaseUrls = true` | [../forbidden-patterns/no-uppercase-routes.md](../forbidden-patterns/no-uppercase-routes.md) |
| Test-specific branches / flags | Delete; if it was needed, re-home to test project | [../forbidden-patterns/no-test-specific-branches.md](../forbidden-patterns/no-test-specific-branches.md) |
| Mocks/stubs wired in DI | Delete; report stub-gen need | [../forbidden-patterns/no-mocks-in-consumer-di.md](../forbidden-patterns/no-mocks-in-consumer-di.md) |
| Seed endpoints / DbInitializer | Delete; report if the removal breaks tests | [../forbidden-patterns/no-seed-endpoints.md](../forbidden-patterns/no-seed-endpoints.md) |
| Persistent Aspire resources | Delete `WithDataVolume`/`ContainerLifetime.Persistent` | [../forbidden-patterns/no-persistent-aspire-resources.md](../forbidden-patterns/no-persistent-aspire-resources.md) |
| Aspire client integrations | Replace with standard client + `GetConnectionString` | [../forbidden-patterns/no-aspire-client-integrations.md](../forbidden-patterns/no-aspire-client-integrations.md) |
| Hardcoded secrets / CS / URLs | Move to config; stop and ask if secret is missing | [../forbidden-patterns/no-hardcoded-secrets.md](../forbidden-patterns/no-hardcoded-secrets.md) |
| `.proto` outside dedicated project | Move to `{Company}.{Product}.gRPC` | [../forbidden-patterns/no-proto-outside-dedicated-project.md](../forbidden-patterns/no-proto-outside-dedicated-project.md) |
| `DateTime.UtcNow` → `TimeProvider.GetUtcNow()` | Swap one-liner | [../forbidden-patterns/no-datetime-utcnow.md](../forbidden-patterns/no-datetime-utcnow.md) |
| `Guid.NewGuid()` for new IDs → `Guid.CreateVersion7()` | Swap one-liner | [../forbidden-patterns/no-non-v7-guids.md](../forbidden-patterns/no-non-v7-guids.md) |
| AutoMapper / Mapperly / Mapster → hand-written `IXxxMapper` (if already adopted elsewhere); MediatR / Mediator (martinothamar) / Brighter → direct service call (default) | Swap | [../forbidden-patterns/no-automapper-no-mediatr.md](../forbidden-patterns/no-automapper-no-mediatr.md) |
| Touching test files from a non-test agent | Stop, report | [../forbidden-patterns/no-touching-test-files.md](../forbidden-patterns/no-touching-test-files.md) |
| Touching AppHost / ServiceDefaults from a consumer | Stop, report | [../forbidden-patterns/no-touching-apphost-from-consumer.md](../forbidden-patterns/no-touching-apphost-from-consumer.md) |

## Scope discipline

The rule applies to files you were **already going to edit** for the primary task. It does NOT authorize opening unrelated files to fix their warts.

If your edit adds ~10 lines to a 500-line file and you find 40 prohibited patterns scattered across it:

- Fix only the ones **inside the methods/regions you touched** plus any that `dotnet build` would complain about.
- Add a note: *"File X has N remaining prohibited patterns outside the scope of my change — recommend a TODO item for a dedicated cleanup pass."*

If removing a pattern cascades into a bigger refactor (e.g., removing a warning suppression reveals 50 nullable warnings):

- STOP the cleanup.
- Do the primary change.
- Surface the cascade as a separate TODO.

## Operational mode

`OPERATIONAL MODE` suspends this rule. The worker performs only the surgical change requested and leaves surrounding code untouched.

## Reporting

Report back alongside what you implemented:

```
Eradicated in pass (scope: files edited):
- Program.cs:42-51  removed `#pragma warning disable CS8600`, fixed root cause
- OrdersController.cs:15  lowercased route `api/Orders` → `api/orders`
- Program.cs:10  removed `env.IsEnvironment("Testing")` branch

Remaining debt in these files (out of scope):
- OrdersController.cs has 3 `AutoMapper` usages — recommend an `IXxxMapper` migration task
```

The cleanup debt shrinks visibly over time when this discipline holds.
