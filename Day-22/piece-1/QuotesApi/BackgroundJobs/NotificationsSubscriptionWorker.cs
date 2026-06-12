using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QuotesApi.Configuration;
using QuotesApi.Data;
using QuotesApi.Messaging;
using System.Text.Json;

namespace QuotesApi.BackgroundJobs;

/// <summary>
/// Consumes the notifications-subscription on quotes-topic.
///
/// Fan-out proof: the same QuoteCreated message published to the topic is delivered
/// independently to BOTH analytics-subscription and notifications-subscription.
/// Each subscription has its own copy of the message and its own delivery count.
///
/// Idempotency: identical to AnalyticsSubscriptionWorker — checks ProcessedMessages
/// keyed on (MessageId, "notifications-subscription") before acting.
/// </summary>
public sealed class NotificationsSubscriptionWorker : BackgroundService
{
    private readonly ServiceBusProcessor                   _processor;
    private readonly IServiceScopeFactory                  _scopeFactory;
    private readonly ILogger<NotificationsSubscriptionWorker> _logger;
    private readonly string                                _subscriptionName;

    public NotificationsSubscriptionWorker(
        ServiceBusClient                          client,
        IOptions<ServiceBusOptions>               opts,
        IServiceScopeFactory                      scopeFactory,
        ILogger<NotificationsSubscriptionWorker>  logger)
    {
        _scopeFactory     = scopeFactory;
        _logger           = logger;
        _subscriptionName = opts.Value.NotificationsSubscription;

        _processor = client.CreateProcessor(
            opts.Value.TopicName,
            _subscriptionName,
            new ServiceBusProcessorOptions
            {
                MaxConcurrentCalls   = 1,
                AutoCompleteMessages = false,
                ReceiveMode          = ServiceBusReceiveMode.PeekLock
            });

        _processor.ProcessMessageAsync += HandleMessageAsync;
        _processor.ProcessErrorAsync   += HandleErrorAsync;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "NotificationsSubscriptionWorker starting — subscription={Subscription}",
            _subscriptionName);

        await _processor.StartProcessingAsync(stoppingToken);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) { }

        _logger.LogInformation("NotificationsSubscriptionWorker stopping");
        await _processor.StopProcessingAsync();
    }

    private async Task HandleMessageAsync(ProcessMessageEventArgs args)
    {
        var messageId = args.Message.MessageId;
        var subject   = args.Message.Subject;

        _logger.LogInformation(
            "[Notifications] Received messageId={MessageId} subject={Subject}",
            messageId, subject);

        // Poison messages are silently dead-lettered here so the notifications
        // queue does not fill up with retries.  The analytics worker carries the
        // DLQ demonstration; here we just log and DLQ cleanly.
        if (subject == "Poison")
        {
            _logger.LogWarning(
                "[Notifications] Poison message {MessageId} — dead-lettering from notifications side",
                messageId);
            await args.DeadLetterMessageAsync(args.Message,
                deadLetterReason: "PoisonMessage",
                deadLetterErrorDescription: "Poison subject, rejected by notifications worker",
                cancellationToken: args.CancellationToken);
            return;
        }

        // Idempotency check
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var alreadyProcessed = await db.ProcessedMessages.AnyAsync(
            p => p.MessageId == messageId && p.SubscriptionName == _subscriptionName,
            args.CancellationToken);

        if (alreadyProcessed)
        {
            _logger.LogWarning(
                "[Notifications] DUPLICATE messageId={MessageId} — skipping",
                messageId);
            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
            return;
        }

        // Business logic
        var payload = JsonSerializer.Deserialize<QuoteCreatedMessage>(
            args.Message.Body.ToArray());

        if (payload is null)
        {
            _logger.LogError("[Notifications] Could not deserialise {MessageId}", messageId);
            await args.DeadLetterMessageAsync(args.Message,
                deadLetterReason: "DeserializationFailure",
                cancellationToken: args.CancellationToken);
            return;
        }

        _logger.LogInformation(
            "[Notifications] NOTIFY user — quoteId={QuoteId} author={Author} (would send email/push here)",
            payload.QuoteId, payload.Author);

        db.ProcessedMessages.Add(new ProcessedMessage
        {
            MessageId        = messageId,
            SubscriptionName = _subscriptionName,
            ProcessedAt      = DateTimeOffset.UtcNow
        });

        try
        {
            await db.SaveChangesAsync(args.CancellationToken);
        }
        catch (DbUpdateException ex) when (IsDuplicateKeyViolation(ex))
        {
            _logger.LogWarning("[Notifications] Race condition on {MessageId} — completing", messageId);
        }

        await args.CompleteMessageAsync(args.Message, args.CancellationToken);
    }

    private Task HandleErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception,
            "[Notifications] ServiceBus processor error — source={ErrorSource} entity={EntityPath}",
            args.ErrorSource, args.EntityPath);
        return Task.CompletedTask;
    }

    private static bool IsDuplicateKeyViolation(DbUpdateException ex)
    {
        var msg = ex.InnerException?.Message ?? string.Empty;
        return msg.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("duplicate", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("unique constraint", StringComparison.OrdinalIgnoreCase);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await _processor.StopProcessingAsync(cancellationToken);
        await base.StopAsync(cancellationToken);
        await _processor.DisposeAsync();
    }
}
