namespace QuotesApi.Messaging;

public interface IQuotePublisher
{
    /// <summary>Publishes a QuoteCreated event to quotes-topic.</summary>
    Task PublishAsync(QuoteCreatedMessage message, CancellationToken ct = default);

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
