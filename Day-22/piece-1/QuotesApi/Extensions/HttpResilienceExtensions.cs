using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.RateLimiting;
using Polly.Retry;
using Polly.Timeout;
using QuotesApi.Resilience;
using System.Threading.RateLimiting;

namespace QuotesApi.Extensions;

/// <summary>
/// Polly v8 resilience pipeline used by every outbound HttpClient in this application.
///
/// Execution order (outer → inner — first added = outermost):
///
///   1. Bulkhead / ConcurrencyLimiter  (PermitLimit 10, QueueLimit 5)
///        Limits how many calls can be in-flight simultaneously.
///        Callers beyond the queue are rejected immediately with
///        RateLimiterRejectedException.
///
///   2. Total timeout (10 s)
///        Hard cap on the complete chain including ALL retries.
///        Prevents a single degraded dependency from occupying a thread forever.
///
///   3. Retry (3 × exponential + jitter, applied to idempotent GET calls)
///        HttpRetryStrategyOptions limits retries to transient HTTP errors
///        (5xx, 408, 429, network exceptions) — safe for GET-only clients.
///        Write operations must NOT use this pipeline without explicit opt-in.
///
///   4. Circuit breaker (50 % failure ratio, min 5 calls, 15 s window, 15 s break)
///        Stops hammering a sick dependency.  Each attempt result (whether from the
///        initial call or a retry) is counted by the circuit breaker individually.
///        Transitions: CLOSED → OPEN → HALF-OPEN (probe) → CLOSED.
///
///   5. Per-attempt timeout (3 s)
///        Kills one slow attempt so the Retry strategy has time to fire a fresh one.
///        Does NOT increment the total-timeout counter — only the outer timeout does.
///
/// Every event (retry, timeout, circuit state change, bulkhead rejection) is logged
/// and incremented in <see cref="ResilienceMetrics"/> so the
/// GET /api/resilience/status endpoint can surface live counters.
/// </summary>
public static class HttpResilienceExtensions
{
    public static void AddDefaultResiliencePipeline(
        this ResiliencePipelineBuilder<HttpResponseMessage> pipeline,
        ILogger logger,
        string pipelineName)
    {
        // ── 1. Bulkhead: concurrency limiter ──────────────────────────────────
        // Uses RateLimiterStrategyOptions so OnRejected can log + count.
        // DefaultRateLimiterOptions creates a ConcurrencyLimiter internally when
        // the RateLimiter delegate is left null (Polly 8.4 behaviour).
        pipeline.AddRateLimiter(new RateLimiterStrategyOptions
        {
            Name = $"{pipelineName}.bulkhead",
            DefaultRateLimiterOptions = new ConcurrencyLimiterOptions
            {
                PermitLimit = 10,
                QueueLimit  = 5,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            },
            OnRejected = args =>
            {
                logger.LogWarning(
                    "Polly bulkhead REJECTED on pipeline {Pipeline} — " +
                    "concurrency limit exceeded (permitLimit=10, queueLimit=5)",
                    pipelineName);
                ResilienceMetrics.IncrementBulkhead();
                return default;
            }
        });

        // ── 2. Total timeout: 10 s ────────────────────────────────────────────
        pipeline.AddTimeout(new TimeoutStrategyOptions
        {
            Name    = $"{pipelineName}.total-timeout",
            Timeout = TimeSpan.FromSeconds(10),
            OnTimeout = args =>
            {
                logger.LogWarning(
                    "Polly TOTAL TIMEOUT on pipeline {Pipeline} — " +
                    "entire chain (including retries) exceeded {Seconds}s",
                    pipelineName,
                    args.Timeout.TotalSeconds);
                ResilienceMetrics.IncrementTimeout();
                return default;
            }
        });

        // ── 3. Retry: 3 × exponential + jitter (idempotent GETs only) ─────────
        // HttpRetryStrategyOptions restricts ShouldHandle to transient HTTP
        // conditions (5xx, 408, 429, network exceptions) so write operations
        // are NOT retried even if this pipeline were accidentally applied to them.
        pipeline.AddRetry(new HttpRetryStrategyOptions
        {
            Name             = $"{pipelineName}.retry",
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
                    "Polly RETRY {Attempt}/{Max} on pipeline {Pipeline} " +
                    "after {DelayMs:F0}ms — outcome: {Outcome}",
                    args.AttemptNumber + 1,
                    3,
                    pipelineName,
                    args.RetryDelay.TotalMilliseconds,
                    outcome);
                ResilienceMetrics.IncrementRetry();
                return default;
            }
        });

        // ── 4. Circuit breaker ────────────────────────────────────────────────
        // Thresholds are tuned for a quick demo while remaining realistic:
        //   MinimumThroughput = 5   → needs at least 5 calls in the sampling window
        //   FailureRatio      = 0.5 → opens when ≥ 50 % of those calls fail
        //   SamplingDuration  = 15s → rolling window
        //   BreakDuration     = 15s → OPEN for 15 s before the HALF-OPEN probe
        //
        // Each individual attempt (including retry attempts) contributes to the
        // circuit breaker's failure count, so a single multi-retry call may
        // account for up to 4 failure samples (1 initial + 3 retries).
        pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            Name              = $"{pipelineName}.cb",
            FailureRatio      = 0.5,
            SamplingDuration  = TimeSpan.FromSeconds(15),
            MinimumThroughput = 5,
            BreakDuration     = TimeSpan.FromSeconds(15),

            OnOpened = args =>
            {
                logger.LogError(
                    "Polly circuit OPEN on {Pipeline} — state: CLOSED → OPEN — " +
                    "break for {BreakSec}s (manual: {Manual})",
                    pipelineName,
                    args.BreakDuration.TotalSeconds,
                    args.IsManual);
                ResilienceMetrics.IncrementCircuitOpened();
                return default;
            },
            OnHalfOpened = _ =>
            {
                logger.LogInformation(
                    "Polly circuit HALF-OPEN on {Pipeline} — state: OPEN → HALF-OPEN — " +
                    "allowing one probe request",
                    pipelineName);
                return default;
            },
            OnClosed = _ =>
            {
                logger.LogInformation(
                    "Polly circuit CLOSED on {Pipeline} — state: HALF-OPEN → CLOSED — " +
                    "dependency recovered",
                    pipelineName);
                return default;
            }
        });

        // ── 5. Per-attempt timeout: 3 s ───────────────────────────────────────
        // Kills one slow attempt so the Retry above has time to send a fresh one
        // before the 10 s total timeout expires.
        // Note: a per-attempt timeout does NOT increment ResilienceMetrics.Timeouts —
        // only the outer 10 s timeout does, to avoid double-counting.
        pipeline.AddTimeout(new TimeoutStrategyOptions
        {
            Name    = $"{pipelineName}.attempt-timeout",
            Timeout = TimeSpan.FromSeconds(3),
            OnTimeout = args =>
            {
                logger.LogWarning(
                    "Polly ATTEMPT TIMEOUT on pipeline {Pipeline} — " +
                    "single attempt exceeded {Seconds}s",
                    pipelineName,
                    args.Timeout.TotalSeconds);
                return default;
            }
        });
    }
}
