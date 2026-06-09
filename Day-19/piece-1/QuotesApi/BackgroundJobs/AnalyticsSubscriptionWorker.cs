using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QuotesApi.Configuration;
using QuotesApi.Data;
using QuotesApi.Messaging;
using System.Text.Json;

namespace QuotesApi.BackgroundJobs;

/// <summary>
/// Competing-consumer worker that drains the analytics-subscription on quotes-topic.
///
/// Competing consumers:
///   MaxConcurrentCalls = 2 creates a pool of two concurrent message handlers within
///   this process.  Service Bus issues a lock per message, so only ONE handler ever
///   processes a given message — the other picks up the next available message.
///   In a scaled-out deployment (multiple pods) the same guarantee holds: each
///   message is delivered to exactly one worker across the entire fleet.
///
/// Idempotency:
///   Before executing business logic the handler checks ProcessedMessages for the
///   (MessageId, SubscriptionName) pair.  If found it completes the message and
///   returns without re-running the logic.  A UNIQUE index in the DB provides an
///   additional race-condition guard for the rare case where a message lock expires
///   mid-flight and Service Bus redelivers to another handler.
///
/// Dead-letter queue:
///   Messages with Subject = "Poison" throw InvalidOperationException.
///   The SDK catches the exception, calls AbandonMessageAsync, and decrements the
///   delivery count.  When MaxDeliveryCount is reached Service Bus automatically
///   moves the message to the $DeadLetterQueue sub-queue.
/// </summary>
public sealed class AnalyticsSubscriptionWorker : BackgroundService
{
    private readonly ServiceBusProcessor              _processor;
    private readonly IServiceScopeFactory             _scopeFactory;
    private readonly ILogger<AnalyticsSubscriptionWorker> _logger;
    private readonly string                           _subscriptionName;

    public AnalyticsSubscriptionWorker(
        ServiceBusClient                       client,
        IOptions<ServiceBusOptions>            opts,
        IServiceScopeFactory                   scopeFactory,
        ILogger<AnalyticsSubscriptionWorker>   logger)
    {
        _scopeFactory     = scopeFactory;
        _logger           = logger;
        _subscriptionName = opts.Value.AnalyticsSubscription;

        _processor = client.CreateProcessor(
            opts.Value.TopicName,
            _subscriptionName,
            new ServiceBusProcessorOptions
            {
                // Two concurrent handlers simulate competing consumers within one process.
                MaxConcurrentCalls   = 2,
                AutoCompleteMessages = false,
                // Receive and delete mode would skip the lock — use PeekLock so we can
                // abandon on failure and let the retry / DLQ cycle run.
                ReceiveMode          = ServiceBusReceiveMode.PeekLock
            });

        _processor.ProcessMessageAsync += HandleMessageAsync;
        _processor.ProcessErrorAsync   += HandleErrorAsync;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "AnalyticsSubscriptionWorker starting — subscription={Subscription} maxConcurrent=2",
            _subscriptionName);

        await _processor.StartProcessingAsync(stoppingToken);

        try
        {
            // Hold here until the host signals shutdown.
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) { }

        _logger.LogInformation("AnalyticsSubscriptionWorker stopping");
        await _processor.StopProcessingAsync();
    }

    private async Task HandleMessageAsync(ProcessMessageEventArgs args)
    {
        var messageId    = args.Message.MessageId;
        var subject      = args.Message.Subject;
        var deliveryCount = args.Message.DeliveryCount;

        // Log thread ID so competing-consumer behaviour is visible in the output.
        _logger.LogInformation(
            "[Analytics-{Thread}] Received messageId={MessageId} subject={Subject} deliveryCount={DeliveryCount}",
            Environment.CurrentManagedThreadId, messageId, subject, deliveryCount);

        // ── Poison message scenario ────────────────────────────────────────────
        // Throw so the SDK calls AbandonMessageAsync.  Service Bus will retry up
        // to MaxDeliveryCount times then move the message to $DeadLetterQueue.
        if (subject == "Poison")
        {
            _logger.LogWarning(
                "[Analytics-{Thread}] POISON message {MessageId} (delivery {DeliveryCount}) — abandoning to trigger DLQ",
                Environment.CurrentManagedThreadId, messageId, deliveryCount);

            throw new InvalidOperationException(
                $"Poison message {messageId} rejected (delivery attempt {deliveryCount}).");
        }

        // ── Idempotency check ──────────────────────────────────────────────────
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var alreadyProcessed = await db.ProcessedMessages.AnyAsync(
            p => p.MessageId == messageId && p.SubscriptionName == _subscriptionName,
            args.CancellationToken);

        if (alreadyProcessed)
        {
            _logger.LogWarning(
                "[Analytics-{Thread}] DUPLICATE messageId={MessageId} — skipping (already in ProcessedMessages)",
                Environment.CurrentManagedThreadId, messageId);
            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
            return;
        }

        // ── Business logic ─────────────────────────────────────────────────────
        var payload = JsonSerializer.Deserialize<QuoteCreatedMessage>(
            args.Message.Body.ToArray());

        if (payload is null)
        {
            _logger.LogError("[Analytics] Could not deserialise body of {MessageId}", messageId);
            await args.DeadLetterMessageAsync(args.Message,
                deadLetterReason: "DeserializationFailure",
                deadLetterErrorDescription: "Body could not be parsed as QuoteCreatedMessage",
                cancellationToken: args.CancellationToken);
            return;
        }

        _logger.LogInformation(
            "[Analytics-{Thread}] PROCESSED quoteId={QuoteId} author={Author} createdAt={CreatedAt:O}",
            Environment.CurrentManagedThreadId, payload.QuoteId, payload.Author, payload.CreatedAt);

        // ── Record idempotency key ─────────────────────────────────────────────
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
            // Another competing consumer won the race and inserted first.
            // Safe to complete — business logic already ran once.
            _logger.LogWarning(
                "[Analytics-{Thread}] Race condition on {MessageId} — completing without re-processing",
                Environment.CurrentManagedThreadId, messageId);
        }

        await args.CompleteMessageAsync(args.Message, args.CancellationToken);
    }

    private Task HandleErrorAsync(ProcessErrorEventArgs args)
    {
        // Called for processor-level errors (connection loss, lock expiry, etc.)
        // and also when ProcessMessageAsync throws (poison-message path).
        _logger.LogError(args.Exception,
            "[Analytics] ServiceBus processor error — source={ErrorSource} entity={EntityPath}",
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
