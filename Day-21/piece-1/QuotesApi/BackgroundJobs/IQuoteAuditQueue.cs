namespace QuotesApi.BackgroundJobs;

public interface IQuoteAuditQueue
{
    /// <summary>Non-blocking enqueue called from the request thread.</summary>
    bool TryEnqueue(QuoteAuditItem item);

    /// <summary>Async stream consumed by the BackgroundService worker.</summary>
    IAsyncEnumerable<QuoteAuditItem> ReadAllAsync(CancellationToken cancellationToken);
}
