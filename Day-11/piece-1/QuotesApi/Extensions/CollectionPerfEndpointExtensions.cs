using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;

namespace QuotesApi.Extensions;

// ── Day 11 — Profile a slow endpoint ─────────────────────────────────────
// Two routes that return the SAME payload but use very different query
// strategies.  The point is to compare them under load and prove that the
// difference is a real N+1 plus a missing index on the join column, not a
// rendering quirk or framework overhead.
//
// Both routes are explicitly AllowAnonymous — they're profiling endpoints,
// not part of the normal Week-1 surface — so k6 can hit them without auth.
public static class CollectionPerfEndpointExtensions
{
    public static IEndpointRouteBuilder MapCollectionPerfEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/collections");

        // ── /slow — deliberate N+1 over items → quote lookup ───────────────
        // For a collection with N items, this emits 1 + N SQL statements:
        //   1 × SELECT for the collection (Include(c => c.Items))
        //   N × SELECT TOP(1) FROM Quotes WHERE Id = @id  (one per item)
        //
        // The .FirstOrDefaultAsync per item is the classic N+1 trigger: each
        // call is a separate round-trip.  Combined with the absence of an
        // index on CollectionItems.QuoteId, every per-item lookup also
        // misses the join column index — so SQL Server resorts to a Clustered
        // Index Seek on Quotes.PK_Quotes (the natural PK), which is OK for
        // a single row but the loop multiplies it 20x per request.
        group.MapGet("/{id:int}/quotes/slow", async (
                int id,
                AppDbContext db,
                CancellationToken cancellationToken) =>
            {
                var collection = await db.Collections
                    .Include(c => c.Items)
                    .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

                if (collection is null)
                    return Results.NotFound();

                // N+1: per-item Quote lookup.  This is the offending pattern.
                var quotes = new List<object>(collection.Items.Count);
                foreach (var item in collection.Items)
                {
                    var quote = await db.Quotes
                        .FirstOrDefaultAsync(q => q.Id == item.QuoteId, cancellationToken);

                    if (quote is not null)
                        quotes.Add(new
                        {
                            id        = quote.Id,
                            author    = quote.Author,
                            text      = quote.Text,
                            createdAt = quote.CreatedAt,
                        });
                }

                return Results.Ok(new
                {
                    collectionId = collection.Id,
                    name         = collection.Name,
                    quotes,
                });
            })
            .AllowAnonymous()
            .WithName("CollectionQuotes_SlowN1");

        // ── /fast — single batched WHERE id IN (...) ───────────────────────
        // Application-level fix: replace the loop with one round-trip that
        // pulls every needed Quote in a single SELECT.  Combined with the
        // index migration on CollectionItems.QuoteId (see Migrations folder)
        // SQL Server can satisfy the entire request with two seeks and one
        // hash/nested-loop join — no per-row round-trip cost.
        group.MapGet("/{id:int}/quotes/fast", async (
                int id,
                AppDbContext db,
                CancellationToken cancellationToken) =>
            {
                var collection = await db.Collections
                    .Include(c => c.Items)
                    .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

                if (collection is null)
                    return Results.NotFound();

                var quoteIds = collection.Items.Select(i => i.QuoteId).ToArray();

                var quotes = await db.Quotes
                    .AsNoTracking()
                    .Where(q => quoteIds.Contains(q.Id))
                    .Select(q => new
                    {
                        id        = q.Id,
                        author    = q.Author,
                        text      = q.Text,
                        createdAt = q.CreatedAt,
                    })
                    .ToListAsync(cancellationToken);

                return Results.Ok(new
                {
                    collectionId = collection.Id,
                    name         = collection.Name,
                    quotes,
                });
            })
            .AllowAnonymous()
            .WithName("CollectionQuotes_FastBatch");

        return app;
    }
}
