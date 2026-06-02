using QuotesApi.Models;
using QuotesApi.Repositories;

namespace QuotesApi.Application.Commands.Collections;

// Command handlers orchestrate side-effecting work: construct or mutate
// domain entities, persist via repositories.  They do NOT contain business
// rules — those live on the entity (see Collection's constructor and
// AddItem method, which throw DomainException for invalid input).
public sealed class CreateCollectionCommandHandler
{
    private readonly ICollectionRepository _repository;

    public CreateCollectionCommandHandler(ICollectionRepository repository)
    {
        _repository = repository;
    }

    public async Task<int> HandleAsync(
        CreateCollectionCommand command,
        CancellationToken cancellationToken = default)
    {
        // Validation happens inside the Collection constructor.  The handler
        // doesn't duplicate the "name 3..80 chars" rule; if the constructor
        // throws, ExceptionMiddleware translates it to HTTP 400.
        var collection = new Collection(command.Name, command.OwnerId);

        var created = await _repository.CreateAsync(collection, cancellationToken);

        // Return ONLY the new id.  Returning the whole entity would leak the
        // write model into the response and violate the read/write split.
        // Callers who want the full view do GET /api/collections/{id}.
        return created.Id;
    }
}
