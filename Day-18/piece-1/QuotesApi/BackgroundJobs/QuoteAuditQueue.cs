using System.Threading.Channels;

namespace QuotesApi.BackgroundJobs;

/// <summary>
/// In-process queue backed by a bounded Channel.
/// The request thread writes with TryEnqueue (never blocks).
/// The BackgroundService worker reads via ReadAllAsync.
/// </summary>
public sealed class QuoteAuditQueue : IQuoteAuditQueue
{
    // Bounded: if the worker falls behind, new audit events are dropped (logged)
    // rather than growing the queue unboundedly and inflating memory.
    private readonly Channel<QuoteAuditItem> _channel =
        Channel.CreateBounded<QuoteAuditItem>(new BoundedChannelOptions(capacity: 1_000)
        {
            FullMode     = BoundedChannelFullMode.DropOldest,
            SingleReader = true,   // only the one BackgroundService reads
            SingleWriter = false   // any request thread may write
        });

    public bool TryEnqueue(QuoteAuditItem item) =>
        _channel.Writer.TryWrite(item);

    public IAsyncEnumerable<QuoteAuditItem> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
