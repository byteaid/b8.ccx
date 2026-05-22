---
name: dotnet-events-exceptions
description: Exception model and event/observer reference for .NET 10 / C# 14. Exceptions: hierarchy + `InnerException`, `AggregateException` (`Flatten`/`Handle`), `throw;` vs `throw ex;`, `ExceptionDispatchInfo` cross-thread rethrow, `ThrowIf*` helpers, `try`/`catch`/`finally`/`when` filters, custom-exception conventions + serialization status (`SYSLIB0050`/`SYSLIB0051`), process-wide events (`UnhandledException`, `UnobservedTaskException`, `FirstChanceException`), COM HRESULT mapping. Events: `event`, `EventHandler<T>`, `OnXxx`/`?.Invoke` raise pattern, custom `add`/`remove` + `EventHandlerList`, weak-event leak mitigations, async-handler caveats, `IObservable<T>`/`IObserver<T>` contract, events vs Rx selection.
when_to_use: |
  - Trigger keywords: throw, try catch, when, exception filter, AggregateException, ExceptionDispatchInfo, ThrowIfNull, ArgumentOutOfRangeException.ThrowIf, custom exception, SYSLIB0050, UnhandledException, UnobservedTaskException, FirstChanceException, HRESULT, event, EventHandler, OnXxx, GetInvocationList, weak event handler, IObservable, IObserver, async void event handler.
  - Task shapes: design custom exception type; choose `throw;` vs `throw ex;` vs wrapping; capture and rethrow across threads; replace manual checks with `ThrowIf*`; install global last-chance logging; handle `AggregateException`; declare and raise an event; convert lambda subscriptions to avoid leaks; implement `IObservable<T>` vs reach for Rx; map HRESULT to managed exception.
allowed-tools: Bash, Edit, Glob, Grep, Monitor, NotebookEdit, PowerShell, Read, Write
user-invocable: false
paths: ["**/*.cs"]
---

# .NET Events and Exceptions — Reference

Reference for raising / catching exceptions and authoring delegate-based events and `IObservable<T>` providers on .NET 10 / C# 14. Cancellation propagation lives in `dotnet-asynchronous-programming`; `Dispose` lifecycle lives in `dotnet-garbage-collection`. Everything else stops here.

## Mental model

- An exception is a **typed** signal that the caller's expectations were violated. Don't use it for control flow.
- An event is a **delegate-backed publish/subscribe** primitive bolted onto a type. Multicast, synchronous, no end-of-stream.
- `IObservable<T>` is a **value-stream contract** with explicit termination (`OnCompleted` / `OnError`). Pick it when composition matters.
- `Exception.StackTrace` is captured at throw time (or via `ExceptionDispatchInfo`); preserve it.

## Non-negotiable rules

1. **`throw;` not `throw ex;`** inside a `catch` (CA2200). Wrapping uses `throw new T(msg, ex);`.
2. **Catch only what you can recover from.** Blanket-catching `Exception` is reserved for top-of-stack boundaries (Main, request pipeline, message pump) where you log + decide to crash or surface.
3. **Don't throw in `finally`** (CA2219). Don't throw from `Equals`/`GetHashCode`/`ToString`/static ctors/operators/`Dispose`/finalizers/event accessors/property getters that look free (CA1065).
4. **Don't throw reserved runtime types** (CA2201): `NullReferenceException`, `IndexOutOfRangeException`, `OutOfMemoryException`, `StackOverflowException`, `AccessViolationException`, `ExecutionEngineException`. Don't throw bare `Exception` / `SystemException`.
5. **Use the `ThrowIf*` helpers** (CA1510 – CA1513): `ArgumentNullException.ThrowIfNull`, `ArgumentException.ThrowIfNullOrEmpty/WhiteSpace`, `ArgumentOutOfRangeException.ThrowIf{Zero,Negative,LessThan,GreaterThan,...}`, `ObjectDisposedException.ThrowIf(_disposed, this)`.
6. **Custom exceptions derive from `Exception`** (not `ApplicationException`). Provide the three canonical constructors. Do not implement binary-serialization constructors in new code (`SYSLIB0050`/`SYSLIB0051`).
7. **Always validate task-returning method arguments synchronously** before the first `await` so the exception is observable without awaiting.
8. **`?.Invoke` is the only safe event-raise idiom.** `ThresholdReached?.Invoke(this, e);` captures the delegate snapshot atomically.
9. **Always unsubscribe events in `Dispose`.** Lambda subscriptions are practically unsubscribable — use a method group when `-=` will be needed.

## Sub-index

| Topic | File | Load when |
|---|---|---|
| Exception hierarchy, `Exception` properties, `InnerException` chaining, `AggregateException`, `ExceptionDispatchInfo` | [exception-model.md](exception-model.md) | Walking causes, flattening aggregates, capturing/rethrowing across threads. |
| `throw;` vs `throw ex;`, `throw` expression, `ThrowIf*` builders, `try`/`catch`/`finally`/`when` filters, async + iterator semantics, CA rules | [throwing-and-catching.md](throwing-and-catching.md) | Writing or auditing throw/catch sites; argument validation; exception filters. |
| Custom exception conventions + canonical .NET exception catalog | [custom-and-common-exceptions.md](custom-and-common-exceptions.md) | Designing a custom exception type; picking the right BCL exception to throw. |
| `AppDomain.UnhandledException`, `UnobservedTaskException`, `FirstChanceException`, host-specific equivalents, COM HRESULT mapping | [process-wide-and-com.md](process-wide-and-com.md) | Installing global last-chance logging; handling/translating COM `HRESULT`. |
| `event` + `EventHandler<T>`, `OnXxx`/`?.Invoke`, custom accessors + `EventHandlerList`, weak-event leak mitigations, async handlers, `IObservable<T>`/`IObserver<T>`, Rx, events vs Rx | [events-and-observable.md](events-and-observable.md) | Declaring/raising/subscribing to events; preventing handler leaks; designing an observer-based stream. |

## Cross-references

- Public docs (Exceptions overview): https://learn.microsoft.com/en-us/dotnet/standard/exceptions/
- Public docs (Best practices): https://learn.microsoft.com/en-us/dotnet/standard/exceptions/best-practices-for-exceptions
- Public docs (`Exception` properties): https://learn.microsoft.com/en-us/dotnet/standard/exceptions/exception-class-and-properties
- Public docs (try/catch): https://learn.microsoft.com/en-us/dotnet/standard/exceptions/how-to-use-the-try-catch-block-to-catch-exceptions
- Public docs (finally): https://learn.microsoft.com/en-us/dotnet/standard/exceptions/how-to-use-finally-blocks
- Public docs (Custom exceptions): https://learn.microsoft.com/en-us/dotnet/standard/exceptions/how-to-create-user-defined-exceptions
- Public docs (COM interop exceptions): https://learn.microsoft.com/en-us/dotnet/standard/exceptions/handling-com-interop-exceptions
- Public docs (`ExceptionDispatchInfo`): https://learn.microsoft.com/en-us/dotnet/api/system.runtime.exceptionservices.exceptiondispatchinfo
- Public docs (`AggregateException`): https://learn.microsoft.com/en-us/dotnet/api/system.aggregateexception
- Public docs (`AppDomain.UnhandledException`): https://learn.microsoft.com/en-us/dotnet/api/system.appdomain.unhandledexception
- Public docs (`TaskScheduler.UnobservedTaskException`): https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.taskscheduler.unobservedtaskexception
- Public docs (Events): https://learn.microsoft.com/en-us/dotnet/standard/events/
- Public docs (Custom event accessors): https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/events/how-to-implement-custom-event-accessors
- Public docs (Observer pattern): https://learn.microsoft.com/en-us/dotnet/standard/events/observer-design-pattern
- Related skill: `dotnet-asynchronous-programming` — `OperationCanceledException`, `await` rethrow semantics, `CancellationToken` propagation.
- Related skill: `dotnet-garbage-collection` — `IDisposable` / `IAsyncDisposable` patterns, finalizers, weak-reference primitives.
- Related skill: `dotnet-parallel-and-threading` — `AggregateException` from `Parallel.For` / PLINQ, `TaskCompletionSource` exception flow.
