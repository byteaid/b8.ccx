# Forbidden — Touching the AppHost or ServiceDefaults from a consumer agent

## What it looks like

A consumer agent (backend / Blazor / test designer) edits a file in the AppHost or ServiceDefaults project:

```csharp
// File: src/Acme.Foo.AppHost/Program.cs
// Edited by `dotnet-sr-developer` to add a resource    ← forbidden

var sql = builder.AddSqlServer("sql");
var api = builder.AddProject<Projects.Acme_Foo_WebAPI>("api")
    .WithReference(sql)
    .WaitFor(sql);
```

```csharp
// File: src/Acme.Foo.ServiceDefaults/Extensions.cs
// Edited by `blazor-sr-developer` to add a custom OpenTelemetry exporter   ← forbidden
```

## Why it's banned

1. **Single owner.** AppHost and ServiceDefaults are owned by the Aspire engineer role. One agent owns the orchestration topology and the cross-cutting concerns. Spreading edits across consumer agents produces inconsistent wiring.
2. **Topology integrity.** Resource registration, `WithReference` graphs, `WaitFor` order, and parameter passing are interdependent. A consumer adding a resource without the engineer's view of the graph creates dangling references or boot-order races.
3. **ServiceDefaults is cross-cutting.** OpenTelemetry, health checks, resilience, and service discovery affect every consumer; changes need a holistic perspective.
4. **Boundary discipline keeps roles clean.** The consumer's contract is "I read `GetConnectionString("name")` and trust the AppHost to inject it." When a consumer also edits the AppHost, the contract collapses.

## Scope rule

| File | Owned by | Consumers may |
|---|---|---|
| `src/{Company}.{Product}.AppHost/Program.cs` | Aspire engineer | read only |
| `src/{Company}.{Product}.AppHost/*.csproj` | Aspire engineer | read only |
| `src/{Company}.{Product}.ServiceDefaults/Extensions.cs` | Aspire engineer | read only |
| `src/{Company}.{Product}.ServiceDefaults/*.csproj` | Aspire engineer | read only |
| `src/{Company}.{Product}.WebAPI/Program.cs` (host) | Backend dev | own; CALL `AddServiceDefaults()` and `MapDefaultEndpoints()` |
| Resources catalog (`AddProject`, `AddSqlServer`, `WithReference`, `WaitFor`) | Aspire engineer | request additions via the orchestrator |

Calling `builder.AddServiceDefaults()` and `app.MapDefaultEndpoints()` from a host's `Program.cs` is fine — those are the consumer's call sites. **Implementing** them is engineer-only.

## What to do instead

If the consumer needs a new resource, a new `WithReference`, a different `WaitFor`, an Aspire client integration (banned — see [no-aspire-client-integrations.md](no-aspire-client-integrations.md)), or a custom OpenTelemetry exporter:

1. **Report the need** with a clear specification: resource type, name, expected connection-string key, consumer that needs it.
2. The orchestrator routes the change to the Aspire engineer.
3. The engineer adds it to the AppHost / ServiceDefaults.
4. The consumer reads the new connection string with `GetConnectionString("<name>")` once the registration lands.

Example report:

> "The new `Acme.Foo.Worker` host needs to consume the existing `gymtrackerdb` SQL Server resource and a new `event-bus` Azure Service Bus emulator. Routing to the Aspire engineer to register the resource and add `WithReference` + `WaitFor` for the worker."

## Enforcement

- **On sight:** if a non-engineer agent finds itself about to edit a file under `src/{Company}.{Product}.AppHost/` or `src/{Company}.{Product}.ServiceDefaults/`, STOP and report.
- **Code review:** PRs that mix consumer code changes with AppHost / ServiceDefaults changes are split into two PRs with separate ownership.

## See also

- [no-aspire-client-integrations.md](no-aspire-client-integrations.md) — the related ban on consumer-side Aspire client integrations.
- `dotnet-aspire` — the Aspire authoring reference.
