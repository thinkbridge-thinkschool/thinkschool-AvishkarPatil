using System.Net;
using System.Net.Http.Json;
using QuotesApi.DTOs;
using Quotes.Tests.Integration.Infrastructure;

namespace Quotes.Tests.Integration.Tests;

/// <summary>
/// Integration tests for /api/collections — create, add item, remove item.
/// </summary>
[Collection("SqlServer")]
public class CollectionsTests : IntegrationTestBase
{
    public CollectionsTests(SqlServerContainerFixture fixture) : base(fixture) { }

    // -----------------------------------------------------------------------
    // POST /api/collections
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CreateCollection_Anonymous_Returns401()
    {
        var response = await Client.PostAsJsonAsync(
            "/api/collections",
            new CreateCollectionRequest("My Favourites"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateCollection_Authenticated_Returns201WithLocation()
    {
        await UseWriterTokenAsync();

        var response = await Client.PostAsJsonAsync(
            "/api/collections",
            new CreateCollectionRequest("My Favourites"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
    }

    // -----------------------------------------------------------------------
    // POST /api/collections/{id}/items?quoteId={quoteId}
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AddItem_ToOwnCollection_Returns200()
    {
        await UseWriterTokenAsync();

        var createResp = await Client.PostAsJsonAsync(
            "/api/collections",
            new CreateCollectionRequest("Stoics"));
        createResp.EnsureSuccessStatusCode();
        var collectionId = createResp.Headers.Location!.OriginalString.Split('/').Last();

        var response = await Client.PostAsync(
            $"/api/collections/{collectionId}/items?quoteId=1", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AddItem_ToAnotherUsersCollection_Returns403()
    {
        // Writer creates a collection.
        await UseWriterTokenAsync();
        var createResp = await Client.PostAsJsonAsync(
            "/api/collections",
            new CreateCollectionRequest("Stoics"));
        createResp.EnsureSuccessStatusCode();
        var collectionId = createResp.Headers.Location!.OriginalString.Split('/').Last();

        // Viewer switches in — they do not own this collection.
        await UseViewerTokenAsync();
        var response = await Client.PostAsync(
            $"/api/collections/{collectionId}/items?quoteId=1", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // -----------------------------------------------------------------------
    // DELETE /api/collections/{id}/items/{quoteId}
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RemoveItem_FromOwnCollection_Returns204()
    {
        await UseWriterTokenAsync();

        // Create collection and add quote 1.
        var createResp = await Client.PostAsJsonAsync(
            "/api/collections",
            new CreateCollectionRequest("Stoics"));
        createResp.EnsureSuccessStatusCode();
        var collectionId = createResp.Headers.Location!.OriginalString.Split('/').Last();

        await Client.PostAsync($"/api/collections/{collectionId}/items?quoteId=1", null);

        // Now remove it.
        var response = await Client.DeleteAsync(
            $"/api/collections/{collectionId}/items/1");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // -----------------------------------------------------------------------
    // Domain boundary tests — DomainException → ExceptionMiddleware → 400
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AddItem_ToNonExistentCollection_Returns404()
    {
        await UseWriterTokenAsync();

        var response = await Client.PostAsync("/api/collections/99999/items?quoteId=1", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RemoveItem_FromNonExistentCollection_Returns404()
    {
        await UseWriterTokenAsync();

        var response = await Client.DeleteAsync("/api/collections/99999/items/1");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RemoveItem_FromAnotherUsersCollection_Returns403()
    {
        // Writer creates collection.
        await UseWriterTokenAsync();
        var createResp = await Client.PostAsJsonAsync(
            "/api/collections",
            new CreateCollectionRequest("Stoics"));
        createResp.EnsureSuccessStatusCode();
        var collectionId = createResp.Headers.Location!.OriginalString.Split('/').Last();

        // Add quote 1 to the collection.
        await Client.PostAsync($"/api/collections/{collectionId}/items?quoteId=1", null);

        // Viewer tries to remove from writer's collection.
        await UseViewerTokenAsync();
        var response = await Client.DeleteAsync(
            $"/api/collections/{collectionId}/items/1");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateCollection_TooShortName_Returns400()
    {
        await UseWriterTokenAsync();

        var response = await Client.PostAsJsonAsync(
            "/api/collections",
            new CreateCollectionRequest("AB"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "Collection name < 3 chars throws DomainException caught by ExceptionMiddleware");
    }

    [Fact]
    public async Task AddItem_Duplicate_Returns400()
    {
        await UseWriterTokenAsync();

        var createResp = await Client.PostAsJsonAsync(
            "/api/collections",
            new CreateCollectionRequest("Stoics"));
        createResp.EnsureSuccessStatusCode();
        var collectionId = createResp.Headers.Location!.OriginalString.Split('/').Last();

        // First add succeeds.
        await Client.PostAsync($"/api/collections/{collectionId}/items?quoteId=1", null);

        // Second add of the same quote must be rejected by the domain model.
        var response = await Client.PostAsync(
            $"/api/collections/{collectionId}/items?quoteId=1", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "Adding the same quote twice throws DomainException caught by ExceptionMiddleware");
    }

    [Fact]
    public async Task RemoveItem_NotInCollection_Returns400()
    {
        await UseWriterTokenAsync();

        var createResp = await Client.PostAsJsonAsync(
            "/api/collections",
            new CreateCollectionRequest("Stoics"));
        createResp.EnsureSuccessStatusCode();
        var collectionId = createResp.Headers.Location!.OriginalString.Split('/').Last();

        // Attempt to remove a quote that was never added.
        var response = await Client.DeleteAsync(
            $"/api/collections/{collectionId}/items/999");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "Removing an absent quote throws DomainException caught by ExceptionMiddleware");
    }
}
