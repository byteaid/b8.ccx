# Resilience — `Microsoft.Extensions.Http.Resilience` + Polly v8

Standard / hedging / custom resilience pipelines for `HttpClient`. Load when adding retries, circuit breakers, hedging, or wiring a pipeline for a static singleton client.

NuGet `Microsoft.Extensions.Http.Resilience`. Built on `Microsoft.Extensions.Resilience` and Polly v8. Add **one** resilience handler per client; do not stack standard + custom.

## Standard pipeline (`AddStandardResilienceHandler`)

Five strategies, outer → inner:

| # | Strategy | Defaults |
|---|---|---|
| 1 | Rate limiter | Permit 1000, queue 0 |
| 2 | Total timeout | 30 s |
| 3 | Retry | Max 3, exponential, jittered, base 2 s |
| 4 | Circuit breaker | Failure ratio 10 %, min throughput 100, sampling 30 s, break 5 s |
| 5 | Attempt timeout | 10 s |

Handled HTTP status codes: 5xx, 408, 429. Handled exceptions: `HttpRequestException`, `TimeoutRejectedException` (Polly — **not** `TimeoutException`).

```csharp
builder.Services.AddHttpClient<ExampleClient>(c => c.BaseAddress = new("https://x"))
    .AddStandardResilienceHandler();

// Disable retry on unsafe methods
httpClientBuilder.AddStandardResilienceHandler(o =>
{
    o.Retry.DisableForUnsafeHttpMethods();   // POST/PATCH/PUT/DELETE/CONNECT
    // or o.Retry.DisableFor(HttpMethod.Post, HttpMethod.Delete);
});
```

## Standard hedging (`AddStandardHedgingHandler`)

Issues parallel requests to multiple endpoints when primary is slow. Pool of per-authority circuit breakers.

| # | Strategy | Defaults |
|---|---|---|
| 1 | Total request timeout | 30 s |
| 2 | Hedging | min 1, max 10, delay 2 s |
| 3 | Rate limiter (per endpoint) | 1000 / 0 |
| 4 | Circuit breaker (per endpoint) | 10 % / 100 / 30 s / 5 s |
| 5 | Attempt timeout (per endpoint) | 10 s |

```csharp
httpClientBuilder.AddStandardHedgingHandler(b =>
{
    b.ConfigureOrderedGroups(o => o.Groups.Add(new UriEndpointGroup
    {
        Endpoints =
        {
            new() { Uri = new("https://example.net/api/experimental"), Weight = 3 },
            new() { Uri = new("https://example.net/api/stable"),       Weight = 97 }
        }
    }));
});
```

Max hedging attempts ≤ number of configured groups.

## Custom Polly v8 pipeline

```csharp
httpClientBuilder.AddResilienceHandler("CustomPipeline", static b =>
{
    b.AddRetry(new HttpRetryStrategyOptions
    {
        BackoffType = DelayBackoffType.Exponential,
        MaxRetryAttempts = 5,
        UseJitter = true
    });
    b.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
    {
        SamplingDuration = TimeSpan.FromSeconds(10),
        FailureRatio = 0.2,
        MinimumThroughput = 3,
        ShouldHandle = static args => ValueTask.FromResult(args is
        {
            Outcome.Result.StatusCode: HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
        })
    });
    b.AddTimeout(TimeSpan.FromSeconds(5));
});
```

## Dynamic reload

```csharp
httpClientBuilder.AddResilienceHandler("AdvancedPipeline",
    static (ResiliencePipelineBuilder<HttpResponseMessage> b, ResilienceHandlerContext ctx) =>
{
    ctx.EnableReloads<HttpRetryStrategyOptions>("RetryOptions");
    var opts = ctx.GetOptions<HttpRetryStrategyOptions>("RetryOptions");
    b.AddRetry(opts);
});
```

```json
{ "RetryOptions": { "Retry": { "BackoffType": "Linear", "UseJitter": false, "MaxRetryAttempts": 7 } } }
```

## Static / singleton client

```csharp
var retryPipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
    .AddRetry(new HttpRetryStrategyOptions { BackoffType = DelayBackoffType.Exponential, MaxRetryAttempts = 3 })
    .Build();

var socketHandler     = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(15) };
var resilienceHandler = new ResilienceHandler(retryPipeline) { InnerHandler = socketHandler };

var httpClient = new HttpClient(resilienceHandler);
```

## Reset / replace

```csharp
services.ConfigureHttpClientDefaults(b => b.AddStandardResilienceHandler());
services.AddHttpClient("custom").RemoveAllResilienceHandlers().AddStandardHedgingHandler();
```

## Known issues

- `Grpc.Net.ClientFactory` ≤ 2.63.0 with `AddStandardResilienceHandler` throws `InvalidOperationException`. Upgrade to 2.64.0+ or suppress with `<SuppressCheckGrpcNetClientFactoryVersion>true</...>`.
- Application Insights ≤ 2.22.0 registered **after** the resilience handler can lose all telemetry. Upgrade to 2.23.0+ or register AppInsights before resilience.
