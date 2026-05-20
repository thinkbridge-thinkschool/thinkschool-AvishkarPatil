using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Models;

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
        _logger.LogInformation("Fetching quotes");

        return await _context.Quotes
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);
    }

    public async Task<Quote?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        return await _context.Quotes
            .FirstOrDefaultAsync(
                q => q.Id == id,
                cancellationToken);
    }

    public async Task<Quote> CreateAsync(
        Quote quote,
        CancellationToken cancellationToken)
    {
        _context.Quotes.Add(quote);

        await _context.SaveChangesAsync(cancellationToken);

        return quote;
    }

    public async Task<bool> DeleteAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var quote = await _context.Quotes
            .FindAsync([id], cancellationToken);

        if (quote == null)
            return false;

        _context.Quotes.Remove(quote);

        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }
}