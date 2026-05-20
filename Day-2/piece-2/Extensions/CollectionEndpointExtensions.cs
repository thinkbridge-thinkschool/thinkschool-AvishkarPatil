using QuotesApi.DTOs;
using QuotesApi.Models;
using QuotesApi.Repositories;

namespace QuotesApi.Extensions;

public static class CollectionEndpointExtensions
{
    public static IEndpointRouteBuilder MapCollectionEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/collections");

        group.MapPost("/", async (
            CreateCollectionRequest request,
            ICollectionRepository repository,
            CancellationToken cancellationToken) =>
        {
            var collection = new Collection(request.Name, request.OwnerId);

            var created = await repository.CreateAsync(
                collection,
                cancellationToken);

            return Results.Created(
                $"/api/collections/{created.Id}",
                created);
        });

        group.MapPost("/{id:int}/items", async (
            int id,
            int quoteId,
            ICollectionRepository repository,
            CancellationToken cancellationToken) =>
        {
            var collection = await repository.GetByIdAsync(id, cancellationToken);
            if (collection is null)
                return Results.NotFound();

            collection.AddItem(quoteId);

            await repository.UpdateAsync(collection, cancellationToken);

            return Results.Ok();
        });

        group.MapDelete("/{id:int}/items/{quoteId:int}", async (
            int id,
            int quoteId,
            ICollectionRepository repository,
            CancellationToken cancellationToken) =>
        {
            var collection = await repository.GetByIdAsync(id, cancellationToken);
            if (collection is null)
                return Results.NotFound();

            collection.RemoveItem(quoteId);

            await repository.UpdateAsync(collection, cancellationToken);

            return Results.NoContent();
        });

        return app;
    }
}
