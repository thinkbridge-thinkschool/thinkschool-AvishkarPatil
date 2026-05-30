using QuotesApi.Exceptions;
using QuotesApi.Repositories;

namespace QuotesApi.Application.Commands.Collections;

public sealed class AddQuoteToCollectionCommandHandler
{
    private readonly ICollectionRepository _repository;

    public AddQuoteToCollectionCommandHandler(ICollectionRepository repository)
    {
        _repository = repository;
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
        // This lives in the handler because it's an application-level
        // policy (who can call this), not a domain invariant (what makes
        // a collection valid).
        if (collection.OwnerId != command.ActingUserId)
            throw new DomainException("Only the owner can modify this collection.");

        // Aggregate invariants (no dup quote, 50-item cap) are enforced on
        // the entity itself — the handler doesn't need to know the rules.
        collection.AddItem(command.QuoteId);

        return await _repository.UpdateAsync(collection, cancellationToken);
    }
}
