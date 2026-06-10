namespace QuotesApi.Messaging;

/// <summary>
/// Canonical message published to quotes-topic whenever a new quote is persisted.
/// The ServiceBusMessage.MessageId (a GUID) is the idempotency key carried by the
/// broker; this record carries the business payload.
/// </summary>
public sealed record QuoteCreatedMessage(
    int            QuoteId,
    string         Author,
    string         Text,
    DateTimeOffset CreatedAt);
