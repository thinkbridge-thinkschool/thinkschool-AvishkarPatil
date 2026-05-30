using System.Security.Claims;
using QuotesApi.Application.Commands.Collections;
using QuotesApi.Application.Queries.Collections;
using QuotesApi.DTOs;

namespace QuotesApi.Extensions;

// Endpoints stay THIN: parse the HTTP request, dispatch to the handler or
// query service, serialise the response.  No business logic, no
// validation, no EF access lives in this file.
//
// The split is visible in the dependencies each route takes:
//   - GET takes ICollectionQueryService       (read-side dependency)
//   - POST takes CreateCollectionCommandHandler (write-side dependency)
// A reader of this file can see at a glance which routes are reads and
// which are writes without reading any code below the signature.
public static class CollectionCqrsEndpointExtensions
{
    public static IEndpointRouteBuilder MapCollectionCqrsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/collections");

        // ── QUERY path ───────────────────────────────────────────────────
        // GET /api/collections/{id} — returns the read model.  Anonymous
        // because a public "view this collection" page typically does not
        // require auth.  Change to .RequireAuthorization() if your app
        // gates reads behind login.
        group.MapGet("/{id:int}", async (
                int                       id,
                ICollectionQueryService   queries,
                CancellationToken         cancellationToken) =>
            {
                var detail = await queries.GetByIdAsync(id, cancellationToken);
                return detail is null ? Results.NotFound() : Results.Ok(detail);
            })
            .AllowAnonymous()
            .WithName("GetCollectionDetail")
            .Produces<CollectionDetailReadModel>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        // ── COMMAND path: create collection ──────────────────────────────
        // POST returns ONLY the new id.  If the caller needs the full
        // read-model view, they GET the new id afterwards.  This keeps
        // command responses minimal and avoids leaking entity shape.
        group.MapPost("/", async (
                CreateCollectionRequest         request,
                ClaimsPrincipal                 user,
                CreateCollectionCommandHandler  handler,
                CancellationToken               cancellationToken) =>
            {
                var ownerId = user.FindFirstValue(ClaimTypes.NameIdentifier)
                              ?? user.FindFirstValue("sub")
                              ?? throw new InvalidOperationException("No subject claim in token.");

                var id = await handler.HandleAsync(
                    new CreateCollectionCommand(request.Name, ownerId),
                    cancellationToken);

                return Results.Created($"/api/collections/{id}", new { id });
            })
            .RequireAuthorization()
            .WithName("CreateCollection");

        // ── COMMAND path: add quote to collection ────────────────────────
        group.MapPost("/{id:int}/items", async (
                int                                 id,
                int                                 quoteId,
                ClaimsPrincipal                     user,
                AddQuoteToCollectionCommandHandler  handler,
                CancellationToken                   cancellationToken) =>
            {
                var actingUserId = user.FindFirstValue(ClaimTypes.NameIdentifier)
                                   ?? user.FindFirstValue("sub")
                                   ?? throw new InvalidOperationException("No subject claim in token.");

                var ok = await handler.HandleAsync(
                    new AddQuoteToCollectionCommand(id, quoteId, actingUserId),
                    cancellationToken);

                return ok ? Results.Ok() : Results.NotFound();
            })
            .RequireAuthorization()
            .WithName("AddQuoteToCollection");

        return app;
    }
}
