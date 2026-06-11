using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using QuotesApi.BackgroundJobs;
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
            IQuoteAuditQueue auditQueue,
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

            // Day-20 (Transactional Outbox): use CreateWithOutboxAsync instead of
            // CreateAsync so that the Quote row and an OutboxMessage row are committed
            // in a single atomic database transaction.
            //
            // We deliberately do NOT call IQuotePublisher here.  The OutboxRelayWorker
            // background service will pick up the unsent outbox row and publish it to
            // Service Bus, typically within seconds.  This separation means:
            //   • A crash between DB commit and Service Bus publish cannot lose the
            //     message — the relay finds the unsent row on restart.
            //   • The HTTP request does not fail if Service Bus is temporarily
            //     unavailable — the message will be delivered once it recovers.
            var created = await repository.CreateWithOutboxAsync(quote, cancellationToken);

            activity?.SetTag("quote.id", created.Id);

            logger.LogInformation(
                "Persisted quote {QuoteId} with outbox record for user {UserId} — relay will publish to Service Bus",
                created.Id, ownerId);

            // Hand off audit work to the background worker — request thread is done.
            auditQueue.TryEnqueue(new QuoteAuditItem(
                QuoteId:   created.Id,
                UserId:    ownerId,
                Author:    created.Author,
                CreatedAt: DateTimeOffset.UtcNow));

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