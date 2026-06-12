using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Messaging;
using QuotesApi.Models;
using QuotesApi.Observability;

namespace QuotesApi.Repositories;

public class QuoteRepository : IQuoteRepository
{
    private readonly AppDbContext _context;
    private readonly ILogger<QuoteRepository> _logger;

    public QuoteRepository(
        AppDbContext context,
        ILogger<QuoteRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<List<Quote>> GetAllAsync(
        int page,
        int size,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching quotes page={Page} size={Size}", page, size);

        using var activity = QuotesTelemetry.Source.StartActivity("list-quotes");
        activity?.SetTag("quotes.page", page);
        activity?.SetTag("quotes.page_size", size);

        // Order newest-first (descending Id) so a freshly created quote — which
        // gets the highest auto-increment Id — lands at the TOP of page 1 and is
        // immediately visible/searchable after a POST + list reload. Without an
        // explicit OrderBy, SQL returns clustered-PK (ascending-Id) order, which
        // pushes new quotes onto the LAST page. An OrderBy before Skip/Take is
        // also required for deterministic paging.
        var quotes = await _context.Quotes
            .Where(q => !q.IsDeleted)
            .OrderByDescending(q => q.Id)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);

        activity?.SetTag("quotes.count", quotes.Count);

        return quotes;
    }

    public async Task<Quote?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        return await _context.Quotes
            .FirstOrDefaultAsync(
                q => q.Id == id && !q.IsDeleted,
                cancellationToken);
    }

    public async Task<Quote> CreateAsync(
        Quote quote,
        CancellationToken cancellationToken)
    {
        _context.Quotes.Add(quote);

        var rows = await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Repository saved {Rows} row(s) for quote {QuoteId}",
            rows, quote.Id);

        return quote;
    }

    public async Task<Quote> CreateWithOutboxAsync(
        Quote quote,
        CancellationToken cancellationToken = default)
    {
        // Open an explicit DB transaction so that both the Quote row and the
        // OutboxMessage row commit atomically.  We need two SaveChangesAsync
        // calls because the first one is what causes the database to assign the
        // auto-increment Quote.Id — only after that can we embed the real Id into
        // the outbox payload.  Without the explicit transaction, a crash between the
        // two SaveChangesAsync calls would leave the Quote persisted but no outbox
        // record, so the Service Bus message would be lost forever.
        await using var tx = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Step 1: persist the Quote so the DB assigns Quote.Id.
            _context.Quotes.Add(quote);
            await _context.SaveChangesAsync(cancellationToken);

            // Step 2: build the outbox payload now that we know the real QuoteId.
            var msg = new QuoteCreatedMessage(
                QuoteId:   quote.Id,
                Author:    quote.Author,
                Text:      quote.Text,
                CreatedAt: DateTimeOffset.UtcNow);

            var outbox = OutboxMessage.Create(
                messageType: "QuoteCreated",
                payload:     JsonSerializer.Serialize(msg));

            // Step 3: persist the outbox row in the same transaction.
            _context.OutboxMessages.Add(outbox);
            await _context.SaveChangesAsync(cancellationToken);

            // Step 4: commit — both rows land in the database together.
            await tx.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "[Repository] Quote {QuoteId} + OutboxMessage {OutboxId} (messageId={MessageId}) committed atomically",
                quote.Id, outbox.Id, outbox.MessageId);

            return quote;
        }
        catch
        {
            // Rollback ensures we never have an orphaned Quote row without an
            // outbox record, which would cause a silent message loss.
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<bool> UpdateAsync(
        Quote quote,
        CancellationToken cancellationToken)
    {
        _context.Quotes.Update(quote);
        return await _context.SaveChangesAsync(cancellationToken) > 0;
    }

}