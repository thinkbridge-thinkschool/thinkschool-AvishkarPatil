namespace QuotesApi.BackgroundJobs;

/// <summary>
/// BackgroundService that drains the QuoteAuditQueue off the request thread.
///
/// Graceful shutdown:
///   1. SIGTERM → ASP.NET Core calls StopAsync(shutdownToken).
///   2. BackgroundService.StopAsync cancels the internal _stoppingCts,
///      which makes the stoppingToken passed to ExecuteAsync fire.
///   3. ReadAllAsync(stoppingToken) sees the cancellation and the
///      await foreach exits via OperationCanceledException.
///   4. ExecuteAsync returns, the host waits (up to ShutdownTimeout,
///      default 5 s) and then proceeds with teardown.
///
/// The catch block re-throws OperationCanceledException so the loop
/// exits immediately instead of silently swallowing the signal.
/// </summary>
public sealed class QuoteAuditWorker : BackgroundService
{
    private readonly IQuoteAuditQueue          _queue;
    private readonly ILogger<QuoteAuditWorker> _logger;

    public QuoteAuditWorker(IQuoteAuditQueue queue, ILogger<QuoteAuditWorker> logger)
    {
        _queue  = queue;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("QuoteAuditWorker started");

        await foreach (var item in _queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessAuditAsync(item, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Shutdown in mid-flight — re-throw so the foreach exits cleanly.
                throw;
            }
            catch (Exception ex)
            {
                // A single bad item must not kill the worker loop.
                _logger.LogError(ex,
                    "Failed to process audit for quote {QuoteId}", item.QuoteId);
            }
        }

        _logger.LogInformation("QuoteAuditWorker stopped");
    }

    private async Task ProcessAuditAsync(QuoteAuditItem item, CancellationToken ct)
    {
        // Simulate slow work that must not block the HTTP request thread:
        // e.g. write to an audit table, send an event to a bus, call an email API.
        await Task.Delay(50, ct);

        _logger.LogInformation(
            "AUDIT | quote {QuoteId} | author {Author} | user {UserId} | created {CreatedAt:O}",
            item.QuoteId, item.Author, item.UserId, item.CreatedAt);
    }
}
