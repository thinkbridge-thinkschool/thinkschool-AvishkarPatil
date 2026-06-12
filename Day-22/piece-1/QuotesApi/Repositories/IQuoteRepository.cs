using QuotesApi.Models;

namespace QuotesApi.Repositories;

public interface IQuoteRepository
{
    Task<List<Quote>> GetAllAsync(
        int page,
        int size,
        CancellationToken cancellationToken);

    Task<Quote?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken);

    Task<Quote> CreateAsync(
        Quote quote,
        CancellationToken cancellationToken);

    /// <summary>
    /// Saves the quote AND an outbox record in a single explicit database transaction.
    /// The outbox row is created internally using the database-assigned Quote.Id so
    /// the payload is always accurate, even when the Quote.Id is an auto-increment key.
    ///
    /// Why an explicit transaction?
    ///   EF Core wraps a single SaveChangesAsync in an implicit transaction, but here
    ///   we need TWO SaveChangesAsync calls: the first one lets the database assign
    ///   Quote.Id (auto-increment PK); only then can we embed that Id into the outbox
    ///   payload.  An explicit transaction brackets both calls so they commit or roll
    ///   back together — this is the atomicity guarantee the Outbox Pattern requires.
    /// </summary>
    Task<Quote> CreateWithOutboxAsync(
        Quote quote,
        CancellationToken cancellationToken = default);

    Task<bool> UpdateAsync(
        Quote quote,
        CancellationToken cancellationToken);
}