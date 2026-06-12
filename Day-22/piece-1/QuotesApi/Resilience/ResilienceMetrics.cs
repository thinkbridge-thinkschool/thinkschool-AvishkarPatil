using System.Diagnostics.Metrics;

namespace QuotesApi.Resilience;

/// <summary>
/// In-process counters for every Polly resilience event.
///
/// Backed by <c>long</c> fields (Interlocked) so they can be read from
/// the /api/resilience/status endpoint.  Each field is also exposed as an
/// OTel ObservableCounter so the values flow to any attached metrics exporter.
/// </summary>
public static class ResilienceMetrics
{
    private static long _retryAttempts;
    private static long _timeouts;
    private static long _circuitOpened;
    private static long _bulkheadRejected;

    public static readonly Meter Meter = new("QuotesApi.Resilience", "1.0");

    static ResilienceMetrics()
    {
        Meter.CreateObservableCounter(
            "resilience_retry_attempts_total",
            () => Volatile.Read(ref _retryAttempts),
            description: "Total Polly retry attempts fired");

        Meter.CreateObservableCounter(
            "resilience_timeout_total",
            () => Volatile.Read(ref _timeouts),
            description: "Total Polly timeout events (total + per-attempt combined)");

        Meter.CreateObservableCounter(
            "resilience_circuit_opened_total",
            () => Volatile.Read(ref _circuitOpened),
            description: "Number of times the circuit breaker has transitioned CLOSED → OPEN");

        Meter.CreateObservableCounter(
            "resilience_bulkhead_rejected_total",
            () => Volatile.Read(ref _bulkheadRejected),
            description: "Number of calls rejected by the concurrency-limiter (bulkhead)");
    }

    public static void IncrementRetry()         => Interlocked.Increment(ref _retryAttempts);
    public static void IncrementTimeout()       => Interlocked.Increment(ref _timeouts);
    public static void IncrementCircuitOpened() => Interlocked.Increment(ref _circuitOpened);
    public static void IncrementBulkhead()      => Interlocked.Increment(ref _bulkheadRejected);

    public static long RetryAttemptCount     => Volatile.Read(ref _retryAttempts);
    public static long TimeoutCount          => Volatile.Read(ref _timeouts);
    public static long CircuitOpenedCount    => Volatile.Read(ref _circuitOpened);
    public static long BulkheadRejectedCount => Volatile.Read(ref _bulkheadRejected);
}
