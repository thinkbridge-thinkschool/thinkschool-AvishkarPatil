using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace QuotesApi.Extensions;

/// <summary>
/// Shared Polly v8 pipeline shape used by every outbound HttpClient in the app.
///
/// Order matters and the order below is deliberate (outer → inner):
///   1. <b>Total timeout (10s)</b> — bounds the whole call including retries.
///   2. <b>Retry (3, jittered exponential)</b> — handles transient 5xx / 408 / 429 / network.
///   3. <b>Circuit breaker (50% over 30s)</b> — stops hammering a sick dependency.
///   4. <b>Per-attempt timeout (3s)</b> — kills a single slow attempt so retry #2 has a chance.
///
/// Logs every retry and every circuit transition. Nothing is silently swallowed.
/// </summary>
public static class HttpResilienceExtensions
{
    public static void AddDefaultResiliencePipeline(
        this ResiliencePipelineBuilder<HttpResponseMessage> pipeline,
        ILogger logger,
        string pipelineName)
    {
        pipeline.AddTimeout(TimeSpan.FromSeconds(10));

        pipeline.AddRetry(new HttpRetryStrategyOptions
        {
            Name           = $"{pipelineName}.retry",
            MaxRetryAttempts = 3,
            BackoffType    = DelayBackoffType.Exponential,
            UseJitter      = true,
            Delay          = TimeSpan.FromMilliseconds(200),
            OnRetry        = args =>
            {
                var outcome = args.Outcome.Result is { } resp
                    ? $"HTTP {(int)resp.StatusCode}"
                    : args.Outcome.Exception?.GetType().Name ?? "unknown";

                logger.LogWarning(
                    "Polly retry {Attempt}/{Max} on pipeline {Pipeline} after {DelayMs}ms — outcome {Outcome}",
                    args.AttemptNumber + 1,
                    3,
                    pipelineName,
                    args.RetryDelay.TotalMilliseconds,
                    outcome);
                return default;
            }
        });

        pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            Name              = $"{pipelineName}.cb",
            FailureRatio      = 0.5,
            SamplingDuration  = TimeSpan.FromSeconds(30),
            MinimumThroughput = 10,
            BreakDuration     = TimeSpan.FromSeconds(30),
            OnOpened          = args =>
            {
                logger.LogError(
                    "Polly circuit OPEN on {Pipeline} for {BreakSec}s (manual: {Manual})",
                    pipelineName,
                    args.BreakDuration.TotalSeconds,
                    args.IsManual);
                return default;
            },
            OnClosed = _ =>
            {
                logger.LogInformation("Polly circuit CLOSED on {Pipeline}", pipelineName);
                return default;
            },
            OnHalfOpened = _ =>
            {
                logger.LogInformation("Polly circuit HALF-OPEN on {Pipeline}", pipelineName);
                return default;
            }
        });

        pipeline.AddTimeout(TimeSpan.FromSeconds(3));
    }
}
