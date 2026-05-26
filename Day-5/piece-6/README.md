# Day 5 · Piece 6 — Polly resilience for outbound HTTP

The Quotes API is built to accept Entra ID tokens alongside its internal JWTs ([Extensions/InfrastructureExtensions.cs:42-79](Extensions/InfrastructureExtensions.cs#L42-L79)). Every Entra-issued token forces the JWT bearer middleware to fetch the OIDC discovery document from `https://login.microsoftonline.com/{tenant}/v2.0/.well-known/openid-configuration` — that fetch is a real outbound dependency on someone else's API. When that call hiccups, every request that needs Entra validation hiccups with it.

This piece wraps the outbound call in [Polly v8](https://www.pollydocs.org/) via `Microsoft.Extensions.Http.Resilience`, with three layered strategies: **retry with jittered exponential backoff**, **circuit breaker**, and **timeouts** (total + per-attempt). A unit test forces transient 503s through the same pipeline and captures the retry log lines as proof.

---

## The wiring

### 1 — Add the package

```bash
dotnet add QuotesApi.csproj package Microsoft.Extensions.Http.Resilience
```

Resolved to `10.6.0`, which pulls in `Polly.Core 8.4.2` underneath.

### 2 — One reusable pipeline shape

Every outbound `HttpClient` in this app gets the same pipeline order. The whole config lives in [Extensions/HttpResilienceExtensions.cs](Extensions/HttpResilienceExtensions.cs), so there is one place to tune defaults:

```csharp
public static void AddDefaultResiliencePipeline(
    this ResiliencePipelineBuilder<HttpResponseMessage> pipeline,
    ILogger logger,
    string pipelineName)
{
    // 1. Outer total timeout — bounds the whole call including retries.
    pipeline.AddTimeout(TimeSpan.FromSeconds(10));

    // 2. Retry: 3 attempts, exponential backoff, jittered.
    pipeline.AddRetry(new HttpRetryStrategyOptions
    {
        MaxRetryAttempts = 3,
        BackoffType      = DelayBackoffType.Exponential,
        UseJitter        = true,
        Delay            = TimeSpan.FromMilliseconds(200),
        OnRetry          = args =>
        {
            var outcome = args.Outcome.Result is { } resp
                ? $"HTTP {(int)resp.StatusCode}"
                : args.Outcome.Exception?.GetType().Name ?? "unknown";
            logger.LogWarning(
                "Polly retry {Attempt}/{Max} on pipeline {Pipeline} after {DelayMs}ms — outcome {Outcome}",
                args.AttemptNumber + 1, 3, pipelineName,
                args.RetryDelay.TotalMilliseconds, outcome);
            return default;
        }
    });

    // 3. Circuit breaker: 50% failures over 30s, opens for 30s.
    pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
    {
        FailureRatio      = 0.5,
        SamplingDuration  = TimeSpan.FromSeconds(30),
        MinimumThroughput = 10,
        BreakDuration     = TimeSpan.FromSeconds(30),
        OnOpened          = args => { logger.LogError(...); return default; },
        OnClosed          = _    => { logger.LogInformation(...); return default; },
        OnHalfOpened      = _    => { logger.LogInformation(...); return default; },
    });

    // 4. Per-attempt timeout — kills one slow attempt so the next retry has room.
    pipeline.AddTimeout(TimeSpan.FromSeconds(3));
}
```

The defaults match the exercise brief:

| Strategy        | Setting                                                   |
|-----------------|-----------------------------------------------------------|
| Total timeout   | 10s (outer)                                               |
| Retry           | 3 attempts, exponential w/ jitter, base delay 200ms       |
| Circuit breaker | 50% failure rate over a 30s sample window, opens for 30s  |
| Per-attempt TO  | 3s (inner)                                                |

### 3 — A typed `HttpClient` for the outbound call

`IEntraIdMetadataClient` ([Services/IEntraIdMetadataClient.cs](Services/IEntraIdMetadataClient.cs)) and its implementation ([Services/EntraIdMetadataClient.cs](Services/EntraIdMetadataClient.cs)) fetch the OIDC discovery document. They're a typed-client so consumers don't depend on `HttpClient` directly and DI picks up the right pre-configured instance.

Wired in [Extensions/InfrastructureExtensions.cs:112-129](Extensions/InfrastructureExtensions.cs#L112-L129):

```csharp
services
    .AddHttpClient<IEntraIdMetadataClient, EntraIdMetadataClient>("entra-id", (sp, client) =>
    {
        var entra  = sp.GetRequiredService<IOptions<EntraIdOptions>>().Value;
        var tenant = string.IsNullOrWhiteSpace(entra.TenantId) ? "common" : entra.TenantId;
        client.BaseAddress = new Uri($"https://login.microsoftonline.com/{tenant}/v2.0/");
    })
    .AddResilienceHandler("default", (pipeline, ctx) =>
    {
        var logger = ctx.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("HttpResilience.entra-id");
        pipeline.AddDefaultResiliencePipeline(logger, pipelineName: "entra-id");
    });
```

### 4 — A diagnostic endpoint

`GET /diagnostics/entra-id-metadata` in [Program.cs:56-69](Program.cs#L56-L69) is the manual smoke test. It calls through the resilient client and returns the issuer claim. Useful for "yank the network and watch logs," not part of any business flow.

---

## The test (forced transient failures)

[Quotes.Tests.Unit/HttpResilienceTests.cs](Quotes.Tests.Unit/HttpResilienceTests.cs) builds the *real* pipeline (same `AddDefaultResiliencePipeline` call) against a stub `HttpMessageHandler` that returns a scripted sequence of responses. An in-memory `ILoggerProvider` captures every log line emitted by the pipeline.

Two scenarios:

1. **Transient — `503, 503, 200`** → the pipeline retries twice and returns the 200 to the caller. Two `Polly retry …` log lines are emitted (one per retry).
2. **Persistent — `503, 503, 503, 503`** → the pipeline exhausts its 3 retries, then returns the last 503 to the caller. The failure is not swallowed; the caller still sees a non-success status.

```bash
dotnet test Quotes.Tests.Unit/Quotes.Tests.Unit.csproj \
    --filter "FullyQualifiedName~HttpResilienceTests" \
    --logger "console;verbosity=detailed"
```

Both tests pass:

```
  Passed Quotes.Tests.Unit.HttpResilienceTests.Persistent_failures_should_exhaust_retries_and_surface_the_last_failure [1 s]
  Passed Quotes.Tests.Unit.HttpResilienceTests.Returns_503_twice_then_200_should_trigger_two_retries_and_succeed [598 ms]

Test Run Successful.
     Passed: 2
```

Full transcript captured at [polly-test-run.log](polly-test-run.log).

### Sample retry log output (transient case)

```
[Information] System.Net.Http.HttpClient.entra-id.LogicalHandler: Start processing HTTP request GET .../openid-configuration
[Information] System.Net.Http.HttpClient.entra-id.ClientHandler: Received HTTP response headers after 0.0029ms - 503
[Warning]     Polly: Resilience event occurred. EventName: 'OnRetry', Source: 'entra-id-default//entra-id.retry', Result: '503'
[Warning]     HttpResilience.entra-id: Polly retry 1/3 on pipeline entra-id after 153.7128ms — outcome HTTP 503
[Information] System.Net.Http.HttpClient.entra-id.ClientHandler: Received HTTP response headers after 0.0054ms - 503
[Warning]     Polly: Resilience event occurred. EventName: 'OnRetry', Source: 'entra-id-default//entra-id.retry', Result: '503'
[Warning]     HttpResilience.entra-id: Polly retry 2/3 on pipeline entra-id after 224.994ms — outcome HTTP 503
[Information] System.Net.Http.HttpClient.entra-id.ClientHandler: Received HTTP response headers after 0.0037ms - 200
[Information] Polly: Execution attempt. Source: 'entra-id-default//entra-id.retry', Result: '200', Handled: 'False', Attempt: '2'
[Information] System.Net.Http.HttpClient.entra-id.LogicalHandler: End processing HTTP request after 397.1901ms - 200
```

Things to notice:

- **Two retry lines, one per failed attempt.** Each carries the attempt number, the jittered backoff delay, the pipeline name, and the outcome.
- **The delays grow.** 153ms then 224ms — exponential base 200ms with jitter applied.
- **The final attempt is `Handled: 'False'`.** The 200 was not treated as a retry-worthy outcome, the pipeline closed out cleanly, the caller sees the 200.
- **Polly's own telemetry runs alongside our custom log.** `Polly: Execution attempt …` lines come for free from `Microsoft.Extensions.Http.Resilience`'s built-in `ResiliencePipelineBuilder` instrumentation. The `HttpResilience.entra-id: Polly retry …` line is the one our `OnRetry` callback writes.

### Sample retry log output (persistent case)

When transient turns into "the dependency is down," the pipeline exhausts retries (1 initial + 3 retries = 4 calls) and surfaces the failure:

```
[Warning] HttpResilience.entra-id: Polly retry 1/3 on pipeline entra-id after 205.99ms — outcome HTTP 503
[Warning] HttpResilience.entra-id: Polly retry 2/3 on pipeline entra-id after 268.27ms — outcome HTTP 503
[Warning] HttpResilience.entra-id: Polly retry 3/3 on pipeline entra-id after 657.07ms — outcome HTTP 503
[Error]   Polly: Execution attempt. Source: 'entra-id-default//entra-id.retry', Result: '503', Handled: 'True', Attempt: '3'
[Information] HttpClient.entra-id.LogicalHandler: End processing HTTP request after 1221.9063ms - 503
```

The total elapsed time (~1.2s) is bounded by the outer 10s timeout — and the per-attempt 3s timeout is what would have rescued us if any single attempt hung instead of failing fast.

---

## Why this order, and not another

Polly composes inside-out: the strategy added *first* is the *outermost* wrapper, the strategy added *last* is *innermost* and runs first on each attempt. So this order:

```
total timeout  →  retry  →  circuit breaker  →  per-attempt timeout
```

means:

- The **per-attempt timeout (3s)** is what each individual `SendAsync` call is bounded by. It exists so that one hung attempt doesn't eat the whole retry budget.
- The **circuit breaker** sees individual outcomes (success / failure / timeout). When the dependency is down hard, retries inside the breaker keep failing, the breaker opens, and *subsequent* calls short-circuit immediately instead of paying the full retry tax.
- The **retry** wraps the breaker — so an opened breaker's `BrokenCircuitException` is treated by retry as a failure (and may itself be retried *after* the break duration, which lets us recover automatically).
- The **outer total timeout (10s)** is the hard ceiling. Even if retries + jittered backoff + breaker latency add up, the caller gets a `TimeoutRejectedException` after 10s and the request never sits forever.

Swap any pair and the behavior changes. Putting the breaker *outside* retry, for example, would mean a single 503 followed by 3 retries each counts as 4 failed samples — the breaker would open much faster than intended. The chosen order matches the [Microsoft.Extensions.Http.Resilience standard pipeline](https://learn.microsoft.com/dotnet/core/resilience/http-resilience#standard-resilience-handler) for exactly this reason. See [WHY.md](WHY.md) for the longer rationale.

---

## What I learned the hard way

- **`AddResilienceHandler` is a separate thing from `AddStandardResilienceHandler`.** The latter is a turn-key bundle with reasonable defaults; the former is custom — it gives you the raw `ResiliencePipelineBuilder<HttpResponseMessage>` and full control. I went custom because the exercise asked for explicit retry + breaker + timeout config, and I wanted to log every retry.
- **`HttpRetryStrategyOptions` already knows what "transient" means.** Its default `ShouldHandle` predicate handles `HttpRequestException`, `TimeoutRejectedException`, and 5xx/408/429 responses. I didn't have to spell that out.
- **`OnRetry` callbacks are async-shaped.** They return `ValueTask` (use `return default;`). The pipeline awaits them before delaying — so if you do something blocking in there, your backoff lies.
- **The library logs to `ILogger` automatically** under categories like `Polly` and `System.Net.Http.HttpClient.{name}`. My `OnRetry` log line is on top of that, but the built-in lines were already enough to satisfy "log every retry, never swallow failures." Both show up in the test transcript.

---

## Files in this piece

- [Extensions/HttpResilienceExtensions.cs](Extensions/HttpResilienceExtensions.cs) — the reusable pipeline shape (retry + breaker + timeouts)
- [Services/IEntraIdMetadataClient.cs](Services/IEntraIdMetadataClient.cs) — interface for the outbound call
- [Services/EntraIdMetadataClient.cs](Services/EntraIdMetadataClient.cs) — typed `HttpClient` consumer
- [Extensions/InfrastructureExtensions.cs:112-129](Extensions/InfrastructureExtensions.cs#L112-L129) — DI wiring: `AddHttpClient<…>().AddResilienceHandler(...)`
- [Program.cs:56-69](Program.cs#L56-L69) — diagnostic endpoint `/diagnostics/entra-id-metadata`
- [Quotes.Tests.Unit/HttpResilienceTests.cs](Quotes.Tests.Unit/HttpResilienceTests.cs) — the forced-failure unit test
- [polly-test-run.log](polly-test-run.log) — full transcript of `dotnet test` showing both retry log lines

---

## Q1 — What did you learn this session?

Polly's value isn't in any one strategy — it's the *pipeline order*. The trick that clicked for me is reading the pipeline inside-out: the strategy added last runs first on each attempt. So `total timeout → retry → breaker → per-attempt timeout` means each attempt is bounded by 3s, retries see those outcomes, the breaker counts them, and the whole thing is still capped at 10s. Once the order is right, the individual strategies become almost interchangeable defaults. The other thing that stuck: `OnRetry` is the contract with operators. If a retry happens silently, a sick dependency looks like high latency and nothing else. So even with the library's built-in `Polly: Resilience event occurred` lines, I'm glad I wrote an explicit `LogWarning` — it carries the pipeline name and the human-readable outcome (`HTTP 503` vs an exception type) instead of just an enum.

## Q2 — What would break this?

The pipeline guards against transient failures and slow dependencies, but it does nothing about **non-idempotent retries**. The OIDC metadata fetch is a `GET`, so retrying it is safe. The moment I reuse this same `AddDefaultResiliencePipeline` on a `POST` to "create payment" or "send email," I'm going to send the request twice (or four times) the next time the dependency flaps with a 503-after-side-effect. The retry strategy can't tell that the server already processed the first attempt before the connection dropped. The fix is per-method: either use idempotency keys, narrow the `ShouldHandle` predicate so retry only fires on errors that *prove* no side effect happened (connect-refused, DNS, pre-headers timeouts) — or skip retry entirely on writes and lean on the circuit breaker plus the user-facing error path. The breaker itself also has a quieter failure mode: `MinimumThroughput = 10` means with low traffic a half-dead dependency never trips the breaker, because there aren't enough samples. Fine for an Entra-validation dependency that gets hit on every request, dangerous for a low-traffic call where you'd want a much smaller threshold.
