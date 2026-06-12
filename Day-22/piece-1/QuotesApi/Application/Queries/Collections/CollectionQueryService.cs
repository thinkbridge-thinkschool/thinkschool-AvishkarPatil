using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using QuotesApi.Data;

namespace QuotesApi.Application.Queries.Collections;

// Reads go directly against AppDbContext — no repository, no aggregate
// materialisation.  AsNoTracking + Select gives EF Core enough information
// to emit a SINGLE SQL statement that produces the exact response shape.
//
// Day-21: wrapped in HybridCache.GetOrCreateAsync so that:
//   L1 — in-process MemoryCache (30 s TTL) absorbs the hottest traffic.
//   L2 — Redis (5 min TTL) survives process restarts and is shared across
//         all application instances.
//   Stampede protection — concurrent misses for the same key share one
//         factory invocation; no DB fan-out.
//
// IServiceScopeFactory is injected instead of AppDbContext directly.
// HybridCache is a Singleton; its factory lambda must not close over a
// Scoped DbContext because the originating HTTP request scope may be
// disposed (client disconnect / cancellation) before the factory completes.
// Creating a child scope inside the factory gives the DB operation its own
// independent lifetime that is always properly disposed.
public sealed class CollectionQueryService : ICollectionQueryService
{
    private readonly IServiceScopeFactory                _scopeFactory;
    private readonly HybridCache                         _cache;
    private readonly ILogger<CollectionQueryService>     _logger;

    public CollectionQueryService(
        IServiceScopeFactory                scopeFactory,
        HybridCache                         cache,
        ILogger<CollectionQueryService>     logger)
    {
        _scopeFactory = scopeFactory;
        _cache        = cache;
        _logger       = logger;
    }

    public async Task<CollectionDetailReadModel?> GetByIdAsync(
        int               id,
        CancellationToken cancellationToken = default)
    {
        // GetOrCreateAsync coalesces all concurrent callers for the same key
        // onto a single factory invocation — the "Cache miss" log line appears
        // exactly once per cache-miss event regardless of concurrency.
        var result = await _cache.GetOrCreateAsync(
            $"collection:{id}",
            async ct =>
            {
                _logger.LogDebug(
                    "Cache miss — fetching collection {CollectionId} from database", id);

                // Own scope keeps the DbContext lifetime independent of the
                // originating HTTP request scope, which may be disposed before
                // this factory completes under stampede coalescence.
                await using var scope = _scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                return await db.Collections
                    .AsNoTracking()
                    .Where(c => c.Id == id)
                    .Select(c => new CollectionDetailReadModel
                    {
                        Id      = c.Id,
                        Name    = c.Name,
                        OwnerId = c.OwnerId,

                        ItemCount = c.Items.Count,

                        LastUpdatedAt = c.Items
                            .OrderByDescending(i => i.AddedAt)
                            .Select(i => (DateTime?)i.AddedAt)
                            .FirstOrDefault(),

                        Quotes = (from i in c.Items
                                  join q in db.Quotes on i.QuoteId equals q.Id
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
                    .FirstOrDefaultAsync(ct);
            },
            cancellationToken: cancellationToken);

        // Do not persist a null result: caching "not found" would hide a
        // collection created after this miss for the remainder of the TTL.
        if (result is null)
            await _cache.RemoveAsync($"collection:{id}", cancellationToken);

        return result;
    }
}
