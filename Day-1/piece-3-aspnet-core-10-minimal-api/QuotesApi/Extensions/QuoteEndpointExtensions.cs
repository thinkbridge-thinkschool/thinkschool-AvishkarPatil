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
            IQuoteRepository repository,
            CancellationToken cancellationToken) =>
        {
            var errors = new Dictionary<string, string[]>();

            if (string.IsNullOrWhiteSpace(request.Author))
                errors["Author"] = ["Author is required"];

            if (string.IsNullOrWhiteSpace(request.Text))
                errors["Text"] = ["Text is required"];

            if (errors.Count > 0)
                return Results.ValidationProblem(errors);

            var quote = new Quote
            {
                Author = request.Author,
                Text = request.Text
            };

            var created = await repository.CreateAsync(
                quote,
                cancellationToken);

            return Results.Created(
                $"/api/quotes/{created.Id}",
                created);
        });

        group.MapDelete("/{id:int}", async (
            int id,
            IQuoteRepository repository,
            CancellationToken cancellationToken) =>
        {
            var deleted = await repository.DeleteAsync(
                id,
                cancellationToken);

            return deleted
                ? Results.NoContent()
                : Results.NotFound();
        });

        return app;
    }
}