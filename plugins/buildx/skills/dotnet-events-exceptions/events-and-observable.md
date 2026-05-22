# Events (Delegate-Based) and `IObservable<T>` / `IObserver<T>`

## Events — declaration

```csharp
public event EventHandler? Connected;                          // no payload
public event EventHandler<MessageReceivedEventArgs>? MessageReceived; // generic payload
```

The `event` keyword restricts invocation to the declaring type; from outside only `+=` / `-=` are allowed. Events can be `static`, `virtual`, `abstract`, `sealed override`, declared in interfaces.

Standard delegates: `EventHandler` / `EventHandler<TEventArgs>`. Custom delegate types only for legacy interop.

## EventArgs

Derive a payload class from `System.EventArgs`; suffix the class name with `EventArgs`. Use `EventArgs.Empty` when no data is needed.

```csharp
public sealed class ThresholdReachedEventArgs : EventArgs
{
    public required int Threshold { get; init; }
    public DateTime TimeReached { get; init; } = DateTime.UtcNow;
}
```

## Canonical raise pattern

Wrap invocation in `protected virtual void OnXxx(EventArgs e)` so derived classes can override:

```csharp
public class Counter
{
    public event EventHandler<ThresholdReachedEventArgs>? ThresholdReached;

    protected virtual void OnThresholdReached(ThresholdReachedEventArgs e)
        => ThresholdReached?.Invoke(this, e);

    public void Add(int x)
    {
        _total += x;
        if (_total >= _threshold)
            OnThresholdReached(new ThresholdReachedEventArgs { Threshold = _threshold });
    }
}
```

`?.Invoke` snapshots the delegate atomically (no torn read between null-check and call). A derived override must call `base.OnXxx(e)` to keep base subscribers working. Field-like events use `Interlocked.CompareExchange` under the hood — concurrent subscribe/unsubscribe is safe.

## Subscribing

```csharp
c.ThresholdReached += OnReached;        // method group
c.ThresholdReached += (s, e) => Log(e); // lambda — practically unsubscribable
c.ThresholdReached -= OnReached;        // unsubscribe
```

## Multicast & invocation order

All handlers run **synchronously** on the calling thread, in subscription order. If one handler throws, **subsequent handlers do not run** unless you iterate manually:

```csharp
var d = ThresholdReached;
if (d is not null)
{
    foreach (EventHandler<ThresholdReachedEventArgs> h in d.GetInvocationList())
    {
        try { h(this, e); } catch (Exception ex) { Log(ex); }
    }
}
```

## Custom `add` / `remove` accessors

When you need behavior beyond the compiler-generated multicast field — interface re-routing, sparse storage for many rarely-raised events, custom locking, weak handlers — write the accessors explicitly. There is no auto-generated backing delegate when you do.

```csharp
public class Drawing : IDrawingObject
{
    private readonly object _objectLock = new();
    private EventHandler? _preDrawEvent;

    event EventHandler IDrawingObject.OnDraw
    {
        add    { lock (_objectLock) _preDrawEvent += value; }
        remove { lock (_objectLock) _preDrawEvent -= value; }
    }
}

// Sparse storage via EventHandlerList
public class Widget
{
    private static readonly object _clickKey = new();
    private static readonly object _hoverKey = new();
    private readonly EventHandlerList _events = new();

    public event EventHandler? Click
    {
        add    => _events.AddHandler(_clickKey, value);
        remove => _events.RemoveHandler(_clickKey, value);
    }
    public event EventHandler? Hover
    {
        add    => _events.AddHandler(_hoverKey, value);
        remove => _events.RemoveHandler(_hoverKey, value);
    }

    protected void OnClick(EventArgs e)
        => (_events[_clickKey] as EventHandler)?.Invoke(this, e);
}
```

`EventHandlerList` trades a per-event delegate field for a hashtable lookup — slightly slower per raise, much smaller per-instance footprint when most events have no subscribers.

## Memory leaks & weak event handlers

A subscription pins the subscriber via the delegate's `Target`. While the publisher lives, the subscriber **cannot be GC'd** — classic leak shape (long-lived publisher + short-lived subscriber).

Mitigations:
1. Always unsubscribe in `Dispose`.
2. Manual weak wrapper holding a `WeakReference<TSubscriber>`.
3. WPF `WeakEventManager<TSource,TEventArgs>` / `IWeakEventListener`.
4. Avoid lambda subscriptions whose closure pins the subscriber — use a method group.

```csharp
public sealed class WeakEventHandler<TSource, TArgs> where TArgs : EventArgs
{
    private readonly WeakReference _targetRef;
    private readonly MethodInfo _method;

    public WeakEventHandler(EventHandler<TArgs> handler)
    {
        _targetRef = new WeakReference(handler.Target);
        _method = handler.Method;
    }

    public EventHandler<TArgs> Handler => Invoke;

    private void Invoke(object? sender, TArgs e)
    {
        var target = _targetRef.Target;
        if (target is null) return;
        _method.Invoke(target, [sender, e]);
    }
}
```

## Async event handlers

`async void` is the standard signature for event handlers because `EventHandler` returns `void`. Exceptions in `async void` are posted to the captured `SynchronizationContext` and become unhandled if not caught **inside** the handler. Always wrap the body:

```csharp
async void OnClicked(object? sender, EventArgs e)
{
    try { await DoWorkAsync(); }
    catch (Exception ex) { Log(ex); /* swallow or post to error UI */ }
}
```

For new APIs that need awaitable handlers, design a custom delegate `Func<object?, TArgs, Task>` and iterate `GetInvocationList()`, awaiting each.

## `IObservable<T>` / `IObserver<T>`

```csharp
public interface IObservable<out T>
{
    IDisposable Subscribe(IObserver<T> observer);
}

public interface IObserver<in T>
{
    void OnNext(T value);
    void OnError(Exception error);
    void OnCompleted();
}
```

| Member | Caller | Role |
|---|---|---|
| `Subscribe(observer)` | observer | Returns `IDisposable` whose `Dispose` unsubscribes. |
| `OnNext(value)` | provider | Push a value. |
| `OnError(ex)` | provider | Terminal: unrecoverable error. No further calls allowed. |
| `OnCompleted()` | provider | Terminal: stream finished cleanly. No further calls allowed. |

### Reference implementation

```csharp
public readonly record struct BaggageInfo(int FlightNumber, string From, int Carousel);

public sealed class BaggageHandler : IObservable<BaggageInfo>
{
    private readonly HashSet<IObserver<BaggageInfo>> _observers = new();
    private readonly HashSet<BaggageInfo> _flights = new();

    public IDisposable Subscribe(IObserver<BaggageInfo> observer)
    {
        if (_observers.Add(observer))
            foreach (var item in _flights) observer.OnNext(item);   // replay state
        return new Unsubscriber(_observers, observer);
    }

    public void Push(BaggageInfo info)
    {
        if (_flights.Add(info))
            foreach (var o in _observers) o.OnNext(info);
    }

    public void Complete()
    {
        foreach (var o in _observers) o.OnCompleted();
        _observers.Clear();
    }

    public void Fail(Exception ex)
    {
        foreach (var o in _observers) o.OnError(ex);
        _observers.Clear();
    }

    private sealed class Unsubscriber(HashSet<IObserver<BaggageInfo>> set, IObserver<BaggageInfo> obs) : IDisposable
    {
        public void Dispose() => set.Remove(obs);
    }
}
```

Contract:
- After `OnError` or `OnCompleted`, the provider **must not** call further methods on that observer.
- Observers should tolerate calls from any thread unless the provider documents otherwise; providers should not assume observers are thread-safe — push from a single sequence per observer.
- `Subscribe` may replay state on subscription or only deliver future values — per-provider design.
- Disposing the returned `IDisposable` after stream completion is a no-op.

### Reactive Extensions (Rx.NET)

`System.Reactive` (NuGet `System.Reactive`) provides LINQ-style operators (`Where`, `Select`, `Throttle`, `Buffer`, `Merge`, `Retry`, `Catch`, `Replay`, …) layered on `IObservable<T>` / `IObserver<T>`. `Subject<T>` / `BehaviorSubject<T>` / `ReplaySubject<T>` are ready-made provider implementations. `Observable.FromEventPattern<TEventArgs>` bridges classic `event`s into observables. Recommended for any non-trivial push pipeline.

### Events vs `IObservable<T>`

| Concern | `event` + delegates | `IObservable<T>` |
|---|---|---|
| Discovery | Member of a type, IDE-visible. | Generic interface; expressed as a returned interface or property. |
| Composition | Manual; no built-in operators. | Rich operators via Rx. |
| Subscription lifetime | `+=` / `-=`. Easy to leak with lambdas. | `IDisposable` from `Subscribe`. RAII-friendly. |
| Termination | None. | `OnCompleted()` / `OnError(ex)`. |
| Error model | Handler exception aborts iteration. | `OnError(ex)` flows through operators. |
| Threading | Synchronous, on raiser's thread. | Same by default; `ObserveOn` / `SubscribeOn` shift threads in Rx. |
| Backpressure | None. | None native; Rx operators (`Buffer`, `Sample`, `Throttle`) approximate it. |
| Multicast | Multicast delegate by default. | Cold by default; `Publish().RefCount()` / `Subject` for hot multicast. |
| Use when | "Something just happened", small subscriber count. | Value streams with composition, transformation, lifecycle, error semantics. |
