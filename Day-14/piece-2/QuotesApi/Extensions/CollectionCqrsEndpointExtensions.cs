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

        // ── QUERY path — EF Core version ────────────────────────────────
        // GET /api/collections/{id}/ef — AsNoTracking + LINQ projection.
        // This is the baseline EF implementation from Day-12 piece-1.
        // Used in the k6 comparison against the Dapper version below.
        group.MapGet("/{id:int}/ef", async (
                int                       id,
                ICollectionQueryService   queries,
                CancellationToken         cancellationToken) =>
            {
                var detail = await queries.GetByIdAsync(id, cancellationToken);
                return detail is null ? Results.NotFound() : Results.Ok(detail);
            })
            .AllowAnonymous()
            .WithName("GetCollectionDetail_EF")
            .Produces<CollectionDetailReadModel>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        // ── QUERY path — Dapper version ──────────────────────────────────
        // GET /api/collections/{id}/dapper — hand-tuned SQL via
        // QueryMultiple.  Identical response shape; different data-access
        // mechanism.  Earns its place because:
        //   - No LINQ expression-tree compilation per request
        //   - No change-tracker surface overhead even at AsNoTracking level
        //   - Explicit SQL — every column and JOIN is visible and auditable
        // Run both under the same k6 load to measure the delta.
        group.MapGet("/{id:int}/dapper", async (
                int                             id,
                ICollectionDapperQueryService   dapper,
                CancellationToken               cancellationToken) =>
            {
                var detail = await dapper.GetByIdAsync(id, cancellationToken);
                return detail is null ? Results.NotFound() : Results.Ok(detail);
            })
            .AllowAnonymous()
            .WithName("GetCollectionDetail_Dapper")
            .Produces<CollectionDetailReadModel>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        // Preserve the original unversioned route pointing at the EF path
        // so Day-12 piece-1 commands (POST /api/collections/) still work.
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
