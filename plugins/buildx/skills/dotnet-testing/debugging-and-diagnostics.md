# Debugging and Diagnostics for Integration Tests

What to do when a `Company.Product.Test` class fails or hangs. Covers the workflow from the first red light through CLI diagnostic tools, IDE attach, and DiagnosticSource subscriptions. Aligned with the team's per-class `DistributedApplication` mount — every recipe assumes Aspire orchestrated topology, MSTest 3.x, and `Blaztrap.Aspire.FileLogging`.

For deeper observability theory (OpenTelemetry, EventPipe internals, OTel SDK wiring), defer to `dotnet-diagnostics`. For log-file plumbing internals (per-resource files, run-id wiring), defer to `dotnet-aspire` § file-logging.

## Failure-triage checklist (apply in order)

1. **Read `TestResults/{run-id}/`** before re-running. Per-resource log files (`{resource}.log`), the `.trx`, and stdout capture live there. Most timeouts are a downstream resource failing to reach `Healthy` — its log will say so.
2. **Confirm the class mounted cleanly.** A failure in `[ClassInitialize]` short-circuits every test in the class. The exception bubbles up as a `ClassInitializeException` on the first test result; check that, not the assertion.
3. **Bump `WaitForResourceHealthyAsync` timeouts to ≥ 3 minutes** for emulated cloud resources. Cosmos / Service Bus / SQL emulators routinely take > 30 s on cold start.
4. **Search the per-resource log for the first `ERR`/`FAIL`/`Exception`.** Working backward from the test assertion is slower than working forward from the first error in the orchestrated process.
5. **Re-run with `Workers = 1, Scope = MethodLevel`** confirmed in `AssemblyInfo.cs`. Class-level parallelism causes intermittent emulator port races.
6. **Attach the debugger** (next section) only when log inspection is exhausted.

## IDE attach

### Visual Studio

- **Test Explorer → right-click → Debug** runs the test under the debugger; breakpoints hit in production code AND test code.
- **Diagnostic Tools window** (Debug → Windows → Diagnostic Tools) — live CPU, memory, exceptions while the test runs.
- **Attach to Process** for an already-running `dotnet test` host: `Debug → Attach to Process…` → filter by `testhost` or the project name.
- **Conditional breakpoints / tracepoints** (right-click breakpoint → Actions) — log values without halting the suite.
- **`launchSettings.json`** in the SUT controls F5 environment for ad-hoc reproduction outside the test runner.

### VS Code

- Install `ms-dotnettools.csdevkit`. Run the **".NET: Debug Test"** command from the command palette on the cursor's test method.
- For attach: `.vscode/launch.json` configuration `{ "type": "coreclr", "request": "attach" }`, then pick the `testhost` process.

### JetBrains Rider

- "Debug current configuration" on a test obeys `launchSettings.json` profiles for the SUT.
- Remote attach over SSH: `Run → Attach to Process… → On remote (SSH)`. Useful when the AppHost spawns containers on a separate dev box.

## .NET diagnostics CLI tools

Install once globally; use against the running AppHost or any spawned project.

```
dotnet tool install --global dotnet-counters
dotnet tool install --global dotnet-trace
dotnet tool install --global dotnet-dump
dotnet tool install --global dotnet-gcdump
dotnet tool install --global dotnet-stack
```

.NET 10 SDK ships `dnx` for ephemeral execution: `dnx dotnet-counters monitor --process-id <pid>`.

### `dotnet-counters` — live metrics

```
dotnet-counters ps
dotnet-counters monitor -p <PID> --refresh-interval 3 --counters System.Runtime
dotnet-counters monitor -p <PID> --counters System.Runtime,Microsoft.AspNetCore.Hosting,Microsoft-AspNetCore-Server-Kestrel
dotnet-counters collect -p <PID> --refresh-interval 3 --format csv -o counters.csv
```

Counter providers worth pinning while a test runs:
- `System.Runtime` — CPU, GC, exceptions, threadpool, working set.
- `Microsoft.AspNetCore.Hosting` — `current-requests`, `requests-per-second`, `failed-requests`.
- `Microsoft-AspNetCore-Server-Kestrel` — connection counts, queue length.
- `System.Net.Http` — outbound `HttpClient` traffic (visible when one project calls another inside the AppHost).

### `dotnet-trace` — EventPipe profiling

```
dotnet-trace ps
dotnet-trace collect -p <PID>                                   # default profile
dotnet-trace collect -p <PID> --profile dotnet-sampled-thread-time,dotnet-common
dotnet-trace collect -p <PID> --providers Microsoft-Extensions-Logging:4:5
dotnet-trace collect -p <PID> --duration 00:00:00:30 -o trace.nettrace
dotnet-trace convert trace.nettrace --format Speedscope -o trace.speedscope.json
```

Built-in profiles:

| Profile | Use |
|---|---|
| `dotnet-common` | Low-overhead runtime events (GC, JIT, AssemblyLoader, Threading, Exceptions). |
| `dotnet-sampled-thread-time` | ~100 Hz managed-thread stack sampling. |
| `gc-verbose` | GC + sampled allocations. |
| `gc-collect` | GC counts only — minimum overhead. |
| `database` | ADO.NET + EF command tracing. |

Stop on a specific event (e.g. capture only until a method JITs):

```
dotnet-trace collect -p <PID> \
  --stopping-event-provider-name Microsoft-Windows-DotNETRuntime \
  --stopping-event-event-name    Method/JittingStarted \
  --stopping-event-payload-filter MethodNameSpace:Program,MethodName:OnButtonClick
```

### `dotnet-dump` — process dumps

```
dotnet-dump ps
dotnet-dump collect -p <PID>                  # default Full
dotnet-dump collect -p <PID> --type Heap -o app.dmp
dotnet-dump analyze app.dmp                   # interactive SOS shell
dotnet-dump analyze app.dmp -c clrstack -c "dumpheap -stat" -c exit
```

Dump types: `Full` (all memory + module images, default), `Heap` (skip mapped images), `Mini` (modules + stacks + exceptions), `Triage` (Mini, PII stripped).

Top SOS commands inside `analyze`:

| Command | Purpose |
|---|---|
| `clrstack` / `clrstack -all` | Managed stacks (current / all threads). |
| `clrthreads` | List managed threads. |
| `dumpheap -stat` | Type histogram on the GC heap. |
| `dumpheap -type X.Y` / `-mt <MT>` | Filter by type / method-table. |
| `gcroot <addr>` | Roots holding an object alive. |
| `dumpasync` | Async state machines on the heap (find a stuck `await`). |
| `parallelstacks` | Merged thread stacks. |
| `syncblk` | Lock holders / waiters (deadlock investigation). |
| `threadpool`, `threadpoolqueue` | Thread-pool stats; thread starvation diagnosis. |
| `pe -lines` | Print exception with line numbers. |

Auto-emit a dump on crash via env vars set on the AppHost or any project resource (`builder.AddProject(...).WithEnvironment(...)`):

| Env var | Purpose |
|---|---|
| `DOTNET_DbgEnableMiniDump=1` | Auto-write a dump on unhandled exception. |
| `DOTNET_DbgMiniDumpType` | `1`=Mini, `2`=Heap, `3`=Triage, `4`=Full. |
| `DOTNET_DbgMiniDumpName` | Output path template (`%d`=pid, `%t`=time). |

Linux container note: dump capture requires `--cap-add=SYS_PTRACE`.

### `dotnet-gcdump` — GC heap snapshot

```
dotnet-gcdump ps
dotnet-gcdump collect -p <PID>          # writes <name>.gcdump
```

Far cheaper than a Heap dump for type/retention analysis. Open in Visual Studio (Memory Usage tool) or PerfView.

### `dotnet-stack` — managed stacks without a dump

```
dotnet-stack ps
dotnet-stack report -p <PID>
```

Use when a test hangs and you only need to know what every thread is currently doing.

## DiagnosticSource events worth subscribing to

Listening directly to `DiagnosticListener` lets a test observe the AppHost's internals without touching production code. Subscribe via `DiagnosticListener.AllListeners.Subscribe` + `[DiagnosticName]` on subscriber methods.

| Source | Notable events |
|---|---|
| `Microsoft.AspNetCore` | `Microsoft.AspNetCore.Hosting.HttpRequestIn{,.Start,.Stop}`, `Microsoft.AspNetCore.Mvc.{BeforeAction,AfterAction}`, `Microsoft.AspNetCore.Diagnostics.UnhandledException`. |
| `HttpHandlerDiagnosticListener` | `System.Net.Http.HttpRequestOut{,.Start,.Stop}`, `System.Net.Http.Exception`. |
| `Microsoft.EntityFrameworkCore` | `Microsoft.EntityFrameworkCore.Database.Command.CommandExecuting/Executed/Error`. |
| `SqlClientDiagnosticListener` | `System.Data.SqlClient.WriteCommandBefore/After`. |
| `Microsoft-Extensions-Logging` (EventSource) | `MessageJson`, `FormatMessage`. |
| `Microsoft-Windows-DotNETRuntime` | GC / JIT / Threading / Contention / Exception runtime ETW. |

For production observability the preferred path is OpenTelemetry instrumentation packages, which auto-translate the same sources into `Activity` + metrics. See `dotnet-diagnostics` for the OTel wire-up.

## Common failure shapes

| Symptom | Likely cause | First action |
|---|---|---|
| `[ClassInitialize]` throws `TimeoutException` waiting on a resource | Healthcheck not yet green; emulator cold start. | Bump `WaitForResourceHealthyAsync` timeout ≥ 3 min; check `{resource}.log` for the first error. |
| First test in the class passes, second hangs | Shared state mistakenly leaked between tests; or the resource is single-threaded and a previous request is still running. | Add per-test cleanup; confirm the resource is healthy via `dotnet-counters` mid-run. |
| Two classes pass individually, fail when run together | Class-level parallelism races on emulator ports. | Confirm `[assembly: Parallelize(Workers = 1, Scope = ExecutionScope.MethodLevel)]` in `AssemblyInfo.cs`. |
| Suite is green locally, red in CI | Time-of-day flake (TLS clock skew, DST), or container image pulled is newer than local cache. | Pin emulator versions in AppHost; rerun in CI with `--logger "console;verbosity=detailed"`. |
| `OperationCanceledException` mid-test with no useful stack | Async stack obscured by `Task.Run` or `.Wait()`. | Rewrite to `await`; use `dotnet-dump analyze ... -c dumpasync` to find the stuck await. |
| Memory grows linearly across tests | A resource registration captures the AppHost or a large object. | Take two `dotnet-gcdump` snapshots 30 s apart; diff in Visual Studio Memory Usage tool. |
| `dotnet-trace` shows huge time in `Microsoft-Extensions-Logging` | Verbose logger with a hot path. | Drop the provider level (`Microsoft-Extensions-Logging:4:2`) or switch to `[LoggerMessage]` source generator in the SUT. |

## Cross-references

- Public docs (.NET diagnostics tools): https://learn.microsoft.com/en-us/dotnet/core/diagnostics/
- Public docs (`dotnet-counters`): https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-counters
- Public docs (`dotnet-trace`): https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-trace
- Public docs (`dotnet-dump`): https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-dump
- Public docs (DiagnosticSource): https://learn.microsoft.com/en-us/dotnet/core/diagnostics/diagnosticsource-diagnosticlistener
- Related skill: `dotnet-aspire` § file-logging — per-resource log capture wiring.
- Related skill: `dotnet-diagnostics` — OTel SDK, EventPipe internals, Application Insights, Prometheus.
- Related skill: `dotnet-conventions` § source-generators/loggermessage — high-performance logging adoption.
