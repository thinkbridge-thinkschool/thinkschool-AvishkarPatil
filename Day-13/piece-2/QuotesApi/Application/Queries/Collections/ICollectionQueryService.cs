namespace QuotesApi.Application.Queries.Collections;

// The query side has its OWN interface, separate from ICollectionRepository.
// Repositories are for the WRITE path: load aggregate → mutate → save.
// Query services bypass repositories entirely and project straight from
// the DbContext into read models.  The two never share methods, and the
// write side cannot accidentally use the query path or vice versa.
//
// This is the core of the CQRS-lite split: different interfaces, different
// shapes, different optimisation targets.
public interface ICollectionQueryService
{
    Task<CollectionDetailReadModel?> GetByIdAsync(
        int               id,
        CancellationToken cancellationToken = default);
}
