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

        // FIX: single query — all columns fetched in one round-trip
        var quotes = await _context.Quotes
            .Where(q => !q.IsDeleted)
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