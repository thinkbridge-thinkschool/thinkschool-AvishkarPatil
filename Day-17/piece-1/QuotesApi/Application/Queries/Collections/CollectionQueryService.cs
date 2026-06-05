using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;

namespace QuotesApi.Application.Queries.Collections;

// Reads go directly against AppDbContext — no repository, no aggregate
// materialisation.  AsNoTracking + Select gives EF Core enough information
// to emit a SINGLE SQL statement that produces the exact response shape.
//
// This is the same projection pattern Day-11 Piece-2's /optimized endpoint
// uses for performance — here it becomes the standard query-path pattern
// for every read in the application.
public sealed class CollectionQueryService : ICollectionQueryService
{
    private readonly AppDbContext _db;

    public CollectionQueryService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<CollectionDetailReadModel?> GetByIdAsync(
        int               id,
        CancellationToken cancellationToken = default)
    {
        // The projection IS the read model.  EF Core translates this into
        // a single SELECT with the necessary JOINs — no entity
        // materialisation, no change tracking, no over-fetching of columns
        // the response never exposes (no IsDeleted, no OwnerId on Quote,
        // no AddedAt duplication, etc.).
        return await _db.Collections
            .AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new CollectionDetailReadModel
            {
                Id      = c.Id,
                Name    = c.Name,
                OwnerId = c.OwnerId,

                // Server-side COUNT — runs in SQL, no rows materialised.
                ItemCount = c.Items.Count,

                // Server-side MAX wrapped as nullable so an empty
                // collection returns null instead of DateTime.MinValue.
                LastUpdatedAt = c.Items
                    .OrderByDescending(i => i.AddedAt)
                    .Select(i => (DateTime?)i.AddedAt)
                    .FirstOrDefault(),

                // LINQ Join → SQL INNER JOIN.  Single statement total, no
                // N+1.  Ordering happens in SQL, not in C#.
                Quotes = (from i in c.Items
                          join q in _db.Quotes on i.QuoteId equals q.Id
                          orderby i.AddedAt
                          select new QuoteSummaryReadModel
                          {
                              Id        = q.Id,
                              Author    = q.Author,
                              Text      = q.Text,
                              CreatedAt = q.CreatedAt,
                              AddedAt   = i.AddedAt,
                          }).ToList(),
            })
            .FirstOrDefaultAsync(cancellationToken);
    }
}
