namespace QuotesApi.Messaging;

public interface IQuotePublisher
{
    /// <summary>Publishes a QuoteCreated event to quotes-topic with an auto-generated MessageId.</summary>
    Task PublishAsync(QuoteCreatedMessage message, CancellationToken ct = default);

    /// <summary>
    /// Publishes a QuoteCreated event with a caller-supplied MessageId.
    /// Used by the OutboxRelayWorker: the MessageId is the stable GUID stored in
    /// the OutboxMessage row, ensuring every relay attempt uses the same id so
    /// Service Bus deduplication and the consumer's ProcessedMessages table can
    /// detect and discard re-deliveries.
    /// </summary>
    Task PublishAsync(QuoteCreatedMessage message, string messageId, CancellationToken ct = default);

    /// <summary>
    /// Publishes a poison message to quotes-topic.
    /// The consumer recognises the Subject="Poison" flag and throws, triggering
    /// Service Bus retries until MaxDeliveryCount is exhausted and the message
    /// lands in the dead-letter queue.
    /// </summary>
    Task PublishPoisonAsync(CancellationToken ct = default);

    /// <summary>
    /// Publishes a QuoteCreated message with a caller-supplied MessageId.
    /// Used to demonstrate idempotency: send the same id twice; the second
    /// delivery is a no-op because ProcessedMessages already has that id.
    /// </summary>
    Task PublishWithIdAsync(string messageId, CancellationToken ct = default);
}
