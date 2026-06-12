using Microsoft.Extensions.Caching.Hybrid;
using QuotesApi.Exceptions;
using QuotesApi.Repositories;

namespace QuotesApi.Application.Commands.Collections;

public sealed class AddQuoteToCollectionCommandHandler
{
    private readonly ICollectionRepository _repository;
    private readonly HybridCache           _cache;

    public AddQuoteToCollectionCommandHandler(
        ICollectionRepository repository,
        HybridCache           cache)
    {
        _repository = repository;
        _cache      = cache;
    }

    // Returns true if the item was added; false if the collection wasn't
    // found.  Domain rule violations (not the owner, already in the
    // collection, > 50 items) bubble as DomainException and become 400s.
    public async Task<bool> HandleAsync(
        AddQuoteToCollectionCommand command,
        CancellationToken cancellationToken = default)
    {
        var collection = await _repository.GetByIdAsync(command.CollectionId, cancellationToken);
        if (collection is null)
            return false;

        // Authorisation rule: only the owner can mutate the collection.
        if (collection.OwnerId != command.ActingUserId)
            throw new DomainException("Only the owner can modify this collection.");

        collection.AddItem(command.QuoteId);

        var ok = await _repository.UpdateAsync(collection, cancellationToken);

        // Evict unconditionally: even when UpdateAsync returns false (zero rows
        // changed — possible on a retry after a prior success) the collection
        // aggregate was already mutated in memory by AddItem.  Keeping a stale
        // cache entry would serve incorrect data for up to the full TTL.
        // RemoveAsync clears both L1 and L2 atomically.
        await _cache.RemoveAsync($"collection:{command.CollectionId}", cancellationToken);

        return ok;
    }
}
