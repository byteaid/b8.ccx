# Load Balancing, Session Affinity, Health Checks, Resilience

Load-balancing policies, session affinity policies and storage, active + passive health checks, rate limiting, timeouts, output caching.

## Load balancing

Cluster field `LoadBalancingPolicy`. Default `PowerOfTwoChoices`.

| Policy | Behavior |
|---|---|
| `FirstAlphabetical` | Alphabetically first available; useful for active/standby. |
| `Random` | Random pick. |
| `PowerOfTwoChoices` (default) | Two random destinations, pick the one with fewer assigned requests. |
| `RoundRobin` | Cyclic. |
| `LeastRequests` | Lowest assigned-request count (examines all destinations). |

Custom: implement `ILoadBalancingPolicy` (`PickDestination(HttpContext, ClusterState, IReadOnlyList<DestinationState>)` returning the picked destination), register as singleton, set `LoadBalancingPolicy = "Name"`.

## Session affinity

Disabled by default. Enable per cluster.

```jsonc
"SessionAffinity": {
  "Enabled": true,
  "Policy": "HashCookie",          // | ArrCookie | Cookie | CustomHeader
  "FailurePolicy": "Redistribute", // | Return503Error
  "AffinityKeyName": "Key1",       // REQUIRED; UNIQUE across clusters
  "Cookie": {
    "Domain":"localhost","Expiration":"03:00:00","HttpOnly":true,"IsEssential":true,
    "MaxAge":"1.00:00:00","Path":"mypath","SameSite":"Strict","SecurePolicy":"Always"
  }
}
```

| Policy | Storage | Protection |
|---|---|---|
| `HashCookie` (default) | Cookie | XxHash64 (obscured, not private). |
| `ArrCookie` | Cookie | SHA-256, IIS ARR-compatible. |
| `Cookie` | Cookie | Encrypted via Data Protection (multi-instance needs Data Protection key sharing). |
| `CustomHeader` | Header | Encrypted via Data Protection. |

Failure: `Redistribute` (default) skips affinity, fall through to LB; `Return503Error` returns 503.

When affinity fronts SignalR / Blazor — load `aspnet-core-signalr` § Scale-out. `aspnet-core-blazor` § Render modes for circuit pinning.

## Health checks

Two states `Active` and `Passive`, both initialized to `Unknown`. Cluster's available destinations rebuilt on any state change.

### Active

```jsonc
"HealthCheck": {
  "AvailableDestinationsPolicy": "HealthyOrPanic",
  "Active": {
    "Enabled": true, "Interval":"00:00:10", "Timeout":"00:00:10",
    "Policy":"ConsecutiveFailures", "Path":"/api/health"
  }
}
```

Probe URI = `Destination.Health` if set else `Destination.Address`, plus `Active.Path` (+ `Query`). Built-in policy `ConsecutiveFailuresHealthPolicy` counts consecutive failures; mark `Unhealthy` once threshold reached. Cluster metadata key `ConsecutiveFailuresHealthPolicy.Threshold` (default `2`).

Active extensibility: `IActiveHealthCheckPolicy.ProbingCompleted(...)`, `IProbingRequestFactory.CreateRequest(...)`. Pipeline: `IActiveHealthCheckMonitor` -> `IProbingRequestFactory` -> HTTP send -> `IActiveHealthCheckPolicy.ProbingCompleted` -> `IDestinationHealthUpdater.SetActive`.

### Passive

```jsonc
"Passive": {
  "Enabled": true, "Policy":"TransportFailureRate", "ReactivationPeriod":"00:00:10"
}
```

Built-in `TransportFailureRateHealthPolicy` — sliding-window failure rate per destination.

| Property | Default |
|---|---|
| `DetectionWindowSize` | `00:01:00` |
| `MinimalTotalCountThreshold` | `10` |
| `DefaultFailureRateLimit` | `0.3` (30%) |

Cluster metadata key `TransportFailureRateHealthPolicy.RateLimit` overrides per-cluster.

`Unhealthy` -> `Unknown` after `ReactivationPeriod`. Unhealthy destinations get **no traffic** (cannot self-recover passively). Passive runs after the response is on the wire — body cannot be intercepted.

Manual pipeline must call `UsePassiveHealthChecks()`; the parameterless `MapReverseProxy()` includes it.

### Available-destination policies

| Policy | Behavior |
|---|---|
| `HealthyAndUnknown` | Excludes any destination with `Active==Unhealthy` or `Passive==Unhealthy`. If both checks disabled, treats as Healthy. Empty -> 503. |
| `HealthyOrPanic` (default) | Calls `HealthyAndUnknown`; if empty, returns ALL destinations. |

## Resilience

### Rate limiting (.NET 7+)

Per-route `RateLimiterPolicy`. Special value `"disable"` opts out.

```csharp
services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("customPolicy", opt =>
    {
        opt.PermitLimit = 4;
        opt.Window      = TimeSpan.FromSeconds(12);
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        opt.QueueLimit  = 2;
    });
});
app.UseRateLimiter();
app.MapReverseProxy();
```

`options.GlobalLimiter = ...` applies regardless of opt-in.

### Timeouts (.NET 8+, YARP 2.1+)

Two timeouts apply simultaneously:

- **`Timeout`** (per-route): total time, `HH:MM:SS`. Mutually exclusive with `TimeoutPolicy` on the same route.
- **`TimeoutPolicy`** (per-route): name from `AddRequestTimeouts`. Special value `"disable"` opts out.
- **`HttpRequest.ActivityTimeout`** (per-cluster, default 100 s): idle timeout. **Always applies** including with debugger attached.

```csharp
services.AddRequestTimeouts(o =>
    o.AddPolicy("customPolicy", TimeSpan.FromSeconds(20)));
app.UseRequestTimeouts();
app.MapReverseProxy();
```

WebSockets: route `Timeout` is **disabled after the WS handshake**; `ActivityTimeout` still applies (use server/client WS keep-alives to prevent it). With debugger attached: route timeouts don't apply, `ActivityTimeout` does.

### Output caching (.NET 7+)

Per-route `OutputCachePolicy`:

```csharp
builder.Services.AddOutputCache(o =>
    o.AddPolicy("customPolicy", b => b.Expire(TimeSpan.FromSeconds(20))));
app.UseOutputCache();
app.MapReverseProxy();
```
