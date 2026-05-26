using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using QuotesApi.DTOs;
using Quotes.Tests.Integration.Infrastructure;

namespace Quotes.Tests.Integration.Tests;

/// <summary>
/// Integration tests for /api/quotes — covering anonymous, authenticated,
/// authorization, and domain-validation paths.
/// </summary>
[Collection("SqlServer")]
public class QuotesTests : IntegrationTestBase
{
    public QuotesTests(SqlServerContainerFixture fixture) : base(fixture) { }

    // -----------------------------------------------------------------------
    // GET /api/quotes  (public, no auth required)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetList_Anonymous_Returns200WithSeededQuote()
    {
        var response = await Client.GetAsync("/api/quotes?page=1&size=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var quotes = await response.Content.ReadFromJsonAsync<JsonElement>();
        quotes.GetArrayLength().Should().BeGreaterThanOrEqualTo(1,
            "DbSeeder always inserts one Marcus Aurelius quote");
    }

    // -----------------------------------------------------------------------
    // GET /api/quotes/{id}  (public, no auth required)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetById_SeededQuote_Returns200()
    {
        var response = await Client.GetAsync("/api/quotes/1");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("author").GetString().Should().Be("Marcus Aurelius");
    }

    [Fact]
    public async Task GetById_UnknownId_Returns404()
    {
        var response = await Client.GetAsync("/api/quotes/999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // -----------------------------------------------------------------------
    // POST /api/quotes  (requires can-edit-quotes policy: scope=quotes.write)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Create_Anonymous_Returns401()
    {
        // No auth header on a fresh client → must be rejected before any policy runs.
        var response = await Client.PostAsJsonAsync(
            "/api/quotes",
            new CreateQuoteRequest { Author = "Seneca", Text = "Errare humanum est." });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Create_AsViewer_Returns403()
    {
        await UseViewerTokenAsync();

        var response = await Client.PostAsJsonAsync(
            "/api/quotes",
            new CreateQuoteRequest { Author = "Seneca", Text = "Dum differtur vita transcurrit." });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "viewer tokens do not carry scope=quotes.write");
    }

    [Fact]
    public async Task Create_AsWriter_Returns201WithLocation()
    {
        await UseWriterTokenAsync();

        var response = await Client.PostAsJsonAsync(
            "/api/quotes",
            new CreateQuoteRequest { Author = "Seneca", Text = "Dum differtur vita transcurrit." });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
    }

    [Fact]
    public async Task Create_EmptyAuthor_Returns400ProblemDetails()
    {
        // Domain model throws DomainException for blank author;
        // ExceptionMiddleware maps it to 400 ProblemDetails.
        await UseWriterTokenAsync();

        var response = await Client.PostAsJsonAsync(
            "/api/quotes",
            new CreateQuoteRequest { Author = "", Text = "valid text" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("status").GetInt32().Should().Be(400);
    }

    // -----------------------------------------------------------------------
    // DELETE /api/quotes/{id}  (requires auth; ownership check inside handler)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Delete_Anonymous_Returns401()
    {
        var response = await Client.DeleteAsync("/api/quotes/1");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Delete_ByNonOwner_Returns403()
    {
        // Quote 1 is seeded and owned by demo (writer). viewer does not own it.
        await UseViewerTokenAsync();

        var response = await Client.DeleteAsync("/api/quotes/1");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "can-delete-own-quote policy rejects users who are not the quote owner");
    }

    [Fact]
    public async Task Delete_UnknownId_Returns404()
    {
        await UseWriterTokenAsync();

        var response = await Client.DeleteAsync("/api/quotes/999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_ByOwner_Returns204()
    {
        // demo owns quote 1 — delete must succeed and be idempotent via soft-delete.
        await UseWriterTokenAsync();

        var response = await Client.DeleteAsync("/api/quotes/1");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
