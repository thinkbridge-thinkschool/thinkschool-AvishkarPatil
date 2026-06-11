using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Messaging;

namespace QuotesApi.BackgroundJobs;

/// <summary>
/// Polls the OutboxMessages table and relays every unsent row to Azure Service Bus.
///
/// Relay strategy:
///   • Runs a tight loop with a configurable PollInterval (default: 10 seconds).
///   • Each cycle opens its own DI scope so the AppDbContext is fresh and scoped
///     correctly.  IQuotePublisher is a singleton and is injected directly.
///   • Takes up to BatchSize rows per cycle (default: 20) ordered by CreatedAt so
///     events are delivered in causal order within a single producer.
///   • Uses the stable OutboxMessage.MessageId as the Service Bus MessageId on every
///     publish attempt.  If the relay crashes mid-batch and a row is re-published on
///     the next restart, Service Bus deduplication (or the consumer's ProcessedMessages
///     table) absorbs the duplicate — this is the "at-least-once" guarantee.
///
/// Mark-as-sent logic:
///   The relay only sets ProcessedAt AFTER SendMessageAsync returns successfully.
///   If the process crashes between SendMessageAsync and the SaveChangesAsync that
///   writes ProcessedAt, the outbox row remains null and the relay re-publishes it.
///   The consumer MUST be idempotent (see AnalyticsSubscriptionWorker / ProcessedMessages).
///
/// Error handling:
///   A single failed row is logged and its Error column is updated, but it does NOT
///   block the rest of the batch — the loop continues to the next row.  The failed
///   row is retried on the next poll cycle.  Persistent failures are visible in the
///   Error column for operator diagnostics.
///
/// Crash-safety proof:
///   1.  Quote + OutboxMessage committed atomically (QuoteRepository.CreateWithOutboxAsync).
///   2.  ProcessedAt is null — relay has not touched this row yet.
///   3.  App crashes (power loss, OOM, deploy restart — any cause).
///   4.  On next startup the relay finds ProcessedAt IS NULL rows.
///   5.  Relay calls PublishAsync with the stored MessageId.
///   6.  Sets ProcessedAt and saves.
///   7.  Message reaches Service Bus — no loss.
/// </summary>
public sealed class OutboxRelayWorker : BackgroundService
{
    private readonly IServiceScopeFactory        _scopeFactory;
    private readonly IQuotePublisher             _publisher;
    private readonly ILogger<OutboxRelayWorker>  _logger;

    private const int PollIntervalSeconds = 10;
    private const int BatchSize           = 20;

    // IQuotePublisher is a singleton — safe to inject directly into this hosted service.
    public OutboxRelayWorker(
        IServiceScopeFactory       scopeFactory,
        IQuotePublisher            publisher,
        ILogger<OutboxRelayWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _publisher    = publisher;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "[OutboxRelay] Worker started — polling every {Interval}s, batch={Batch}",
            PollIntervalSeconds, BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RelayPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Log unexpected errors (e.g. DB unavailable) but keep polling so
                // that messages are delivered as soon as the dependency recovers.
                _logger.LogError(ex, "[OutboxRelay] Unexpected error during relay cycle — will retry in {Interval}s",
                    PollIntervalSeconds);
            }

            await Task.Delay(TimeSpan.FromSeconds(PollIntervalSeconds), stoppingToken);
        }

        _logger.LogInformation("[OutboxRelay] Worker stopped");
    }

    private async Task RelayPendingAsync(CancellationToken ct)
    {
        // Create a fresh scope per cycle so the AppDbContext lifetime is bounded
        // and does not accumulate change-tracked objects across cycles.
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Fetch only unsent rows, oldest-first, up to BatchSize.
        var pending = await db.OutboxMessages
            .Where(m => m.ProcessedAt == null)
            .OrderBy(m => m.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (pending.Count == 0)
            return;

        _logger.LogInformation("[OutboxRelay] Relaying {Count} outbox message(s)", pending.Count);

        foreach (var outbox in pending)
        {
            try
            {
                var msg = JsonSerializer.Deserialize<QuoteCreatedMessage>(outbox.Payload)
                    ?? throw new InvalidOperationException(
                        $"Outbox row {outbox.Id} payload could not be deserialized.");

                // Use the stable MessageId stored in the outbox row so that every
                // re-delivery attempt carries the same id — consumers and Service Bus
                // can detect and discard duplicates via that id.
                await _publisher.PublishAsync(msg, outbox.MessageId, ct);

                // Mark as sent ONLY after the broker acknowledged the message.
                // If the process crashes here before SaveChangesAsync, ProcessedAt
                // stays null and the row is re-published on the next restart —
                // at-least-once delivery is preserved.
                outbox.MarkSent(DateTime.UtcNow);

                _logger.LogInformation(
                    "[OutboxRelay] Relayed outbox {OutboxId} messageId={MessageId} quoteId={QuoteId}",
                    outbox.Id, outbox.MessageId, msg.QuoteId);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Host is shutting down — stop the batch cleanly.  Unsent rows
                // will be picked up on the next startup.
                throw;
            }
            catch (Exception ex)
            {
                // One bad row does not block the rest of the batch.
                outbox.RecordError(ex.Message);
                _logger.LogWarning(
                    ex,
                    "[OutboxRelay] Failed to relay outbox {OutboxId} — error recorded, will retry next cycle",
                    outbox.Id);
            }
        }

        // Persist all MarkSent / RecordError updates in a single round-trip.
        await db.SaveChangesAsync(ct);
    }
}
