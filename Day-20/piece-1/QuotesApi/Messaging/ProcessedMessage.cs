namespace QuotesApi.Messaging;

/// <summary>
/// Idempotency record: one row per (MessageId, SubscriptionName) pair that has been
/// successfully processed.  A UNIQUE index on those two columns is the concurrency
/// guard — if two competing consumers race, the second SaveChanges throws
/// DbUpdateException and the handler completes the message without re-executing
/// business logic.
/// </summary>
public sealed class ProcessedMessage
{
    public int            Id               { get; set; }
    public string         MessageId        { get; set; } = string.Empty;
    public string         SubscriptionName { get; set; } = string.Empty;
    public DateTimeOffset ProcessedAt      { get; set; }
}
