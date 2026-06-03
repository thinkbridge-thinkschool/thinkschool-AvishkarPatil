namespace QuotesApi.Application.Commands.Collections;

// A command is intent.  It is not an entity and not a DTO that mirrors the
// database — it describes an operation the caller wants performed.  Records
// keep it immutable and serialisation-friendly.
//
// OwnerId is captured at the edge (the endpoint reads it from the JWT and
// passes it in) so the handler doesn't depend on HttpContext.
public sealed record CreateCollectionCommand(string Name, string OwnerId);
