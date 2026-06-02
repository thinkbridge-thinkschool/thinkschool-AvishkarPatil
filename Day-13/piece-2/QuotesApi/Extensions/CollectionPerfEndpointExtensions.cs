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

        // ── /optimized — Day-11 Piece-2 production fix ─────────────────────
        // Goal: drop p99 by ≥ 10× vs /slow.
        //
        // Changes vs /slow (the N+1 baseline) and vs /fast (the partial fix):
        //
        //   1.  ONE SQL statement, not two.
        //         /fast still issues:
        //           SELECT collection JOIN items     (round-trip 1)
        //           SELECT quotes WHERE Id IN (...)  (round-trip 2)
        //         /optimized issues:
        //           SELECT collection LEFT JOIN items LEFT JOIN quotes  (round-trip 1)
        //         Saves one network RTT per request.  Under 20 VUs × 30 s
        //         that's ~30 ms × 1400 iterations = ~42 s of cumulative latency.
        //
        //   2.  No entity tracking anywhere — AsNoTracking on the root query
        //         means EF never materialises a Collection entity, never
        //         allocates EntityEntry / ISnapshot, never inserts into the
        //         identity map.  See Day-10 piece-1 for the tax this avoids.
        //
        //   3.  No intermediate domain object — the projection lands directly
        //         in the response shape.  Nothing in C# loops over Items;
        //         the JOIN + ORDER BY happen entirely on SQL Server.
        //
        //   4.  Index added to CollectionItems(QuoteId) — see
        //         AppDbContext.OnModelCreating + sql/fix-add-index.sql.
        //         This is the sys.dm_db_missing_index_details recommendation
        //         that surfaced after Piece-1's k6 baseline.  It converts the
        //         Clustered Index Scan that SQL Server would otherwise pick
        //         for the items-side of the JOIN into an Index Seek.
        //
        // Response shape is IDENTICAL to /slow and /fast so the same k6 checks
        // and the same downstream consumers continue to work unchanged.
        group.MapGet("/{id:int}/quotes/optimized", async (
                int id,
                AppDbContext db,
                CancellationToken cancellationToken) =>
            {
                var result = await db.Collections
                    .AsNoTracking()
                    .Where(c => c.Id == id)
                    .Select(c => new
                    {
                        collectionId = c.Id,
                        name         = c.Name,
                        quotes       = (from i in c.Items
                                        join q in db.Quotes on i.QuoteId equals q.Id
                                        orderby i.AddedAt
                                        select new
                                        {
                                            id        = q.Id,
                                            author    = q.Author,
                                            text      = q.Text,
                                            createdAt = q.CreatedAt,
                                        }).ToList(),
                    })
                    .FirstOrDefaultAsync(cancellationToken);

                return result is null ? Results.NotFound() : Results.Ok(result);
            })
            .AllowAnonymous()
            .WithName("CollectionQuotes_Optimized");

        return app;
    }
}
