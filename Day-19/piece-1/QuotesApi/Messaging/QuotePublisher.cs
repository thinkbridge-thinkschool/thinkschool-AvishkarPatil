using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using QuotesApi.Configuration;
using System.Text.Json;

namespace QuotesApi.Messaging;

/// <summary>
/// Publishes messages to the quotes-topic Azure Service Bus topic.
/// Registered as a singleton: ServiceBusSender is thread-safe and should be
/// reused across requests rather than created per-request.
/// </summary>
public sealed class QuotePublisher : IQuotePublisher, IAsyncDisposable
{
    private readonly ServiceBusSender             _sender;
    private readonly ILogger<QuotePublisher>      _logger;

    public QuotePublisher(
        ServiceBusClient           client,
        IOptions<ServiceBusOptions> opts,
        ILogger<QuotePublisher>    logger)
    {
        _sender = client.CreateSender(opts.Value.TopicName);
        _logger = logger;
    }

    public async Task PublishAsync(QuoteCreatedMessage message, CancellationToken ct = default)
    {
        var messageId = Guid.NewGuid().ToString();
        var body      = JsonSerializer.SerializeToUtf8Bytes(message);

        var sbMessage = new ServiceBusMessage(body)
        {
            MessageId   = messageId,
            Subject     = "QuoteCreated",
            ContentType = "application/json"
        };

        await _sender.SendMessageAsync(sbMessage, ct);

        _logger.LogInformation(
            "[Publisher] Sent QuoteCreated messageId={MessageId} quoteId={QuoteId} topic={Topic}",
            messageId, message.QuoteId, _sender.EntityPath);
    }

    public async Task PublishPoisonAsync(CancellationToken ct = default)
    {
        var messageId = Guid.NewGuid().ToString();

        // Body is intentionally unparseable JSON so any consumer that tries
        // to deserialise it also fails, reinforcing the poison scenario.
        var sbMessage = new ServiceBusMessage("__POISON_MESSAGE__")
        {
            MessageId   = messageId,
            Subject     = "Poison",
            ContentType = "text/plain"
        };

        await _sender.SendMessageAsync(sbMessage, ct);

        _logger.LogWarning(
            "[Publisher] Sent POISON messageId={MessageId} — expect retries then DLQ",
            messageId);
    }

    public async Task PublishWithIdAsync(string messageId, CancellationToken ct = default)
    {
        var msg = new QuoteCreatedMessage(
            QuoteId:   88888,
            Author:    "Idempotency Demo",
            Text:      "Send this twice to prove the second delivery is a no-op.",
            CreatedAt: DateTimeOffset.UtcNow);

        var body      = JsonSerializer.SerializeToUtf8Bytes(msg);
        var sbMessage = new ServiceBusMessage(body)
        {
            MessageId   = messageId,
            Subject     = "QuoteCreated",
            ContentType = "application/json"
        };

        await _sender.SendMessageAsync(sbMessage, ct);

        _logger.LogInformation(
            "[Publisher] Sent QuoteCreated messageId={MessageId} (caller-supplied) quoteId={QuoteId}",
            messageId, msg.QuoteId);
    }

    public async ValueTask DisposeAsync() => await _sender.DisposeAsync();
}
