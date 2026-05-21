using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using QuotesApi.DTOs;
using QuotesApi.Models;
using QuotesApi.Repositories;

namespace QuotesApi.Extensions;

public static class QuoteEndpointExtensions
{
    public static IEndpointRouteBuilder MapQuoteEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/quotes");

        group.MapGet("/", async (
            int page,
            int size,
            IQuoteRepository repository,
            CancellationToken cancellationToken) =>
        {
            var quotes = await repository.GetAllAsync(
                page,
                size,
                cancellationToken);

            return Results.Ok(quotes);
        });

        group.MapGet("/{id:int}", async (
            int id,
            IQuoteRepository repository,
            CancellationToken cancellationToken) =>
        {
            var quote = await repository.GetByIdAsync(
                id,
                cancellationToken);

            return quote is null
                ? Results.NotFound()
                : Results.Ok(quote);
        });

        group.MapPost("/", async (
            CreateQuoteRequest request,
            ClaimsPrincipal user,
            IQuoteRepository repository,
            CancellationToken cancellationToken) =>
        {
            var ownerIdStr = user.FindFirstValue(ClaimTypes.NameIdentifier)
                             ?? user.FindFirstValue("sub");
            var ownerId = int.TryParse(ownerIdStr, out var id) ? id : (int?)null;

            var quote = Quote.Create(request.Author, request.Text, ownerId);

            var created = await repository.CreateAsync(quote, cancellationToken);

            return Results.Created($"/api/quotes/{created.Id}", created);
        }).RequireAuthorization("can-edit-quotes");

        group.MapDelete("/{id:int}", async (
            int id,
            ClaimsPrincipal user,
            IAuthorizationService authz,
            IQuoteRepository repository,
            CancellationToken cancellationToken) =>
        {
            var quote = await repository.GetByIdAsync(id, cancellationToken);

            if (quote is null)
                return Results.NotFound();

            var result = await authz.AuthorizeAsync(user, quote, "can-delete-own-quote");
            if (!result.Succeeded)
                return Results.Forbid();

            quote.SoftDelete();
            await repository.UpdateAsync(quote, cancellationToken);

            return Results.NoContent();
        }).RequireAuthorization();

        return app;
    }
}