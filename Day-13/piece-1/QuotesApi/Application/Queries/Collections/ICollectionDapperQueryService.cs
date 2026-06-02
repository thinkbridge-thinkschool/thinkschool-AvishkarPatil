namespace QuotesApi.Application.Queries.Collections;

// Day-12 piece-2 — Dapper implementation of the same read contract.
// Both implementations return CollectionDetailReadModel so callers
// can swap the data-access mechanism without changing the endpoint or
// the response shape.  The interface is intentionally separate from
// ICollectionQueryService so both can be registered side-by-side and
// compared under load.
public interface ICollectionDapperQueryService
{
    Task<CollectionDetailReadModel?> GetByIdAsync(
        int               id,
        CancellationToken cancellationToken = default);
}
