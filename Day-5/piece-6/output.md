# Day 5 · Piece 6 — Polly resilience for outbound HTTP

The Quotes API validates Entra ID tokens, which forces an outbound fetch of the OIDC discovery document at `https://login.microsoftonline.com/{tenant}/v2.0/.well-known/openid-configuration`. That outbound call is the dependency this piece wraps in Polly.

The longer write-up (why this pipeline order, what I learned the hard way) is in [README.md](README.md) and [WHY.md](WHY.md). This file is the exercise submission.

---

## 1. HttpClient registration

[Extensions/InfrastructureExtensions.cs:112-129](Extensions/InfrastructureExtensions.cs#L112-L129)

```csharp
// Outbound HTTP to Entra ID (OIDC metadata fetch).
// Anything that calls another API gets the same resilience pipeline:
// total timeout → retry → circuit breaker → per-attempt timeout.
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

## 2. Resilience handler config

[Extensions/HttpResilienceExtensions.cs](Extensions/HttpResilienceExtensions.cs) — one reusable shape applied to every outbound `HttpClient` in the app.

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
        OnOpened          = args => { logger.LogError(
            "Polly circuit OPEN on {Pipeline} for {BreakSec}s (manual: {Manual})",
            pipelineName, args.BreakDuration.TotalSeconds, args.IsManual); return default; },
        OnClosed          = _    => { logger.LogInformation("Polly circuit CLOSED on {Pipeline}", pipelineName); return default; },
        OnHalfOpened      = _    => { logger.LogInformation("Polly circuit HALF-OPEN on {Pipeline}", pipelineName); return default; },
    });

    // 4. Per-attempt timeout — kills one slow attempt so the next retry has room.
    pipeline.AddTimeout(TimeSpan.FromSeconds(3));
}
```

| Strategy        | Setting                                                   |
|-----------------|-----------------------------------------------------------|
| Total timeout   | 10s (outermost)                                           |
| Retry           | 3 attempts, exponential w/ jitter, base delay 200ms       |
| Circuit breaker | 50% failure rate over a 30s sample window, opens for 30s  |
| Per-attempt TO  | 3s (innermost)                                            |

## 3. Test — forced transient failure

[Quotes.Tests.Unit/HttpResilienceTests.cs](Quotes.Tests.Unit/HttpResilienceTests.cs) builds the *real* pipeline (same `AddDefaultResiliencePipeline` call) against a stub `HttpMessageHandler` that returns `503, 503, 200`, with an in-memory `ILoggerProvider` capturing every log line.

```csharp
[Fact]
public async Task Returns_503_twice_then_200_should_trigger_two_retries_and_succeed()
{
    var stub = new ScriptedHandler(
        new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
        new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
        new HttpResponseMessage(HttpStatusCode.OK));

    var sink = new InMemoryLogProvider();
    var services = new ServiceCollection();
    services.AddLogging(b => { b.SetMinimumLevel(LogLevel.Debug); b.AddProvider(sink); });

    services.AddHttpClient("entra-id")
        .ConfigurePrimaryHttpMessageHandler(() => stub)
        .AddResilienceHandler("default", (pipeline, ctx) =>
        {
            var logger = ctx.ServiceProvider.GetRequiredService<ILoggerFactory>()
                .CreateLogger("HttpResilience.entra-id");
            pipeline.AddDefaultResiliencePipeline(logger, pipelineName: "entra-id");
        });

    await using var sp = services.BuildServiceProvider();
    var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("entra-id");
    http.BaseAddress = new Uri("https://login.microsoftonline.com/common/v2.0/");

    var response = await http.GetAsync(".well-known/openid-configuration");

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    stub.Calls.Should().Be(3, "1 initial attempt + 2 retries before the 200");

    var retryLines = sink.Entries
        .Where(e => e.Message.StartsWith("Polly retry", StringComparison.Ordinal))
        .ToList();
    retryLines.Should().HaveCount(2);
    retryLines.Should().AllSatisfy(e =>
    {
        e.LogLevel.Should().Be(LogLevel.Warning);
        e.Message.Should().Contain("HTTP 503");
        e.Message.Should().Contain("pipeline entra-id");
    });
}
```

A second test (`Persistent_failures_should_exhaust_retries_and_surface_the_last_failure`) forces `503` on all four attempts and asserts the pipeline exhausts retries and returns the last 503 to the caller — the failure is **not** silently swallowed.

```bash
dotnet test Quotes.Tests.Unit/Quotes.Tests.Unit.csproj \
    --filter "FullyQualifiedName~HttpResilienceTests" \
    --logger "console;verbosity=detailed"
```

```
  Passed Quotes.Tests.Unit.HttpResilienceTests.Persistent_failures_should_exhaust_retries_and_surface_the_last_failure [1 s]
  Passed Quotes.Tests.Unit.HttpResilienceTests.Returns_503_twice_then_200_should_trigger_two_retries_and_succeed [598 ms]

Test Run Successful.
     Passed: 2
```

Full transcript: [polly-test-run.log](polly-test-run.log).

## 4. Retry log output

### Transient case (`503, 503, 200`)

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
- **Two retry lines, one per failed attempt** — attempt number, jittered backoff delay, pipeline name, outcome (`HTTP 503`).
- **The delays grow.** 153 ms then 224 ms — exponential base 200 ms with jitter.
- **The final attempt is `Handled: 'False'`** — the 200 closes out cleanly, caller gets a 200.

### Persistent case (`503, 503, 503, 503`)

```
[Warning] HttpResilience.entra-id: Polly retry 1/3 on pipeline entra-id after 205.99ms — outcome HTTP 503
[Warning] HttpResilience.entra-id: Polly retry 2/3 on pipeline entra-id after 268.27ms — outcome HTTP 503
[Warning] HttpResilience.entra-id: Polly retry 3/3 on pipeline entra-id after 657.07ms — outcome HTTP 503
[Error]   Polly: Execution attempt. Source: 'entra-id-default//entra-id.retry', Result: '503', Handled: 'True', Attempt: '3'
[Information] HttpClient.entra-id.LogicalHandler: End processing HTTP request after 1221.9063ms - 503
```

After 3 retries (1 initial + 3 = 4 calls) the pipeline gives up and returns the last 503 to the caller. Total elapsed ~1.2 s, well under the outer 10 s budget.

---

## GitHub link

- **Repository:** [https://github.com/thinkbridge-thinkschool/thinkschool-AvishkarPatil](https://github.com/thinkbridge-thinkschool/thinkschool-AvishkarPatil)
- **Folder:** [Day-5/piece-6](https://github.com/thinkbridge-thinkschool/thinkschool-AvishkarPatil/tree/main/Day-5/piece-6)

---

## Q1 — What did you learn this session?

Polly's value isn't in any one strategy — it's the *pipeline order*. The trick that clicked for me is reading the pipeline inside-out: the strategy added **last** runs **first** on each attempt. So `total timeout → retry → breaker → per-attempt timeout` means each attempt is bounded by 3 s, retries see those outcomes, the breaker counts them, and the whole thing is still capped at 10 s. Once that order is right, the individual strategies become almost interchangeable defaults — the hard thinking is in the layering, not in the knob values.

The other thing that stuck: **`OnRetry` is the contract with operators.** If a retry happens silently, a sick dependency looks like high latency and nothing else. Even with the library's built-in `Polly: Resilience event occurred` lines, I'm glad I wrote an explicit `LogWarning` in the callback — it carries the pipeline name and the human-readable outcome (`HTTP 503` vs an exception type) instead of just an enum. That's the line I'd actually grep for in a real outage.

## Q2 — What would break this?

The pipeline guards against transient failures and slow dependencies, but it does nothing about **non-idempotent retries**. The OIDC metadata fetch is a `GET`, so retrying it is safe. The moment I reuse this same `AddDefaultResiliencePipeline` on a `POST` to "create payment" or "send email," I'm going to send the request two or four times the next time the dependency flaps with a 503-after-side-effect. The retry strategy can't tell that the server already processed the first attempt before the connection dropped. The fix is per-method: use idempotency keys on writes, narrow the `ShouldHandle` predicate so retry only fires on errors that *prove* no side effect happened (connect-refused, DNS NXDOMAIN, pre-headers timeouts), or skip retry entirely on writes and lean on the breaker plus the user-facing error path.

A quieter failure mode lives in the breaker config: `MinimumThroughput = 10` means with low traffic a half-dead dependency never trips the breaker, because there aren't enough samples in the 30 s window. That's fine for an Entra-validation dependency that gets hit on every request, but dangerous for a low-traffic call where you'd want a much smaller threshold (or a `MinimumThroughput = 2` with a tighter sampling window). Defaults that "work for most services" stop working the moment the traffic profile changes.
