using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
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

    public async Task<bool> UpdateAsync(
        Quote quote,
        CancellationToken cancellationToken)
    {
        _context.Quotes.Update(quote);
        return await _context.SaveChangesAsync(cancellationToken) > 0;
    }

}