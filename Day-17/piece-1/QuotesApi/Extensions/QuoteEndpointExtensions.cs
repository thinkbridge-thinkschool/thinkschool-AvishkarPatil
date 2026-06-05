using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using QuotesApi.DTOs;
using QuotesApi.Models;
using QuotesApi.Observability;
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
        }).RequireAuthorization("mi-read");   // Day-17: requires the broker's Managed-Identity token

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
        }).RequireAuthorization("mi-read");   // Day-17: requires the broker's Managed-Identity token

        group.MapPost("/", async (
            CreateQuoteRequest request,
            ClaimsPrincipal user,
            IQuoteRepository repository,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            using var activity = QuotesTelemetry.Source.StartActivity("create-quote");

            var logger = loggerFactory.CreateLogger("QuotesApi.Quotes");

            var ownerIdStr = user.FindFirstValue(ClaimTypes.NameIdentifier)
                             ?? user.FindFirstValue("sub");
            var ownerId = int.TryParse(ownerIdStr, out var id) ? id : (int?)null;

            activity?.SetTag("user.id", ownerId);
            activity?.SetTag("quote.author", request.Author);
            activity?.SetTag("quote.text.length", request.Text?.Length ?? 0);

            logger.LogInformation(
                "Creating quote for user {UserId} by author {Author}",
                ownerId, request.Author);

            var quote = Quote.Create(request.Author, request.Text, ownerId);

            logger.LogInformation(
                "Quote built in memory with author {Author} and length {TextLength}",
                quote.Author, quote.Text.Length);

            var created = await repository.CreateAsync(quote, cancellationToken);

            activity?.SetTag("quote.id", created.Id);

            logger.LogInformation(
                "Persisted quote {QuoteId} for user {UserId}",
                created.Id, ownerId);

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