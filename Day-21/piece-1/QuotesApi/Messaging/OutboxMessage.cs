using System.Text.Json;

namespace QuotesApi.Messaging;

/// <summary>
/// A persisted record of a domain event that must be published to Service Bus.
///
/// The Transactional Outbox Pattern writes this row in the SAME database transaction
/// as the domain change (e.g. a new Quote row).  A background relay then reads
/// unprocessed rows, publishes each one to Service Bus, and marks the row as sent.
///
/// Column design rationale:
///   Id          — surrogate PK, auto-increment; determines relay processing order.
///   MessageType — discriminator for the relay (e.g. "QuoteCreated") so future event
///                 types can share the same table without ambiguity.
///   Payload     — the full JSON of the event.  Serialised once at write time so the
///                 relay never has to re-query the domain entity.
///   MessageId   — stable GUID chosen at write time and reused as the Service Bus
///                 MessageId on every publish attempt.  This makes the relay
///                 idempotent on the broker side: if the relay crashes and re-publishes
///                 the same row, Service Bus deduplication (or the consumer's
///                 ProcessedMessages table) absorbs the duplicate.
///   CreatedAt   — ordering signal for the relay; records in CreatedAt order to
///                 preserve causal ordering within a single producer.
///   ProcessedAt — null = unsent (relay must process); non-null = successfully
///                 published and acknowledged.  The relay filters on this column.
///   Error       — last error message from a failed publish attempt, for diagnostics.
///                 The row is retried on the next poll cycle regardless.
/// </summary>
public sealed class OutboxMessage
{
    public int       Id          { get; private set; }
    public string    MessageType { get; private set; } = string.Empty;
    public string    Payload     { get; private set; } = string.Empty;

    // Chosen once at write time; reused on every publish attempt so Service Bus
    // and any idempotent consumer can detect and discard duplicate deliveries.
    public string    MessageId   { get; private set; } = string.Empty;

    public DateTime  CreatedAt   { get; private set; }

    // Null until the relay successfully calls SendMessageAsync and saves.
    public DateTime? ProcessedAt { get; private set; }

    // Last publish error, if any.  Informational — row is retried regardless.
    public string?   Error       { get; private set; }

    // EF Core requires a parameterless constructor.
    private OutboxMessage() { }

    public static OutboxMessage Create(string messageType, string payload)
        => new()
        {
            MessageType = messageType,
            Payload     = payload,
            MessageId   = Guid.NewGuid().ToString(),
            CreatedAt   = DateTime.UtcNow
        };

    /// <summary>Call after a successful Service Bus publish.</summary>
    public void MarkSent(DateTime processedAt)
    {
        ProcessedAt = processedAt;
        Error       = null;
    }

    /// <summary>
    /// Records the error from a failed publish attempt so operators can diagnose
    /// repeated failures.  The relay will retry the row on the next poll cycle.
    /// </summary>
    public void RecordError(string error) => Error = error;
}
