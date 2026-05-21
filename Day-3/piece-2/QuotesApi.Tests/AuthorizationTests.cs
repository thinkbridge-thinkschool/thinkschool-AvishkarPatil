using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Data;
using QuotesApi.DTOs;

namespace QuotesApi.Tests;

public class AuthorizationTests : IClassFixture<AuthorizationTests.InMemoryFactory>, IDisposable
{
    public sealed class InMemoryFactory : WebApplicationFactory<Program>, IDisposable
    {
        private readonly SqliteConnection _connection;

        public InMemoryFactory()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                if (descriptor is not null)
                    services.Remove(descriptor);

                services.AddDbContext<AppDbContext>(options =>
                    options.UseSqlite(_connection));
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) _connection.Dispose();
        }
    }

    private readonly HttpClient _client;

    public AuthorizationTests(InMemoryFactory factory)
    {
        _client = factory.CreateClient();
    }

    public void Dispose() => _client.Dispose();

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private async Task<string> LoginAsync(string email)
    {
        var response = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(email, "P@ssw0rd!"));
        response.EnsureSuccessStatusCode();
        var body = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
        return body.AccessToken;
    }

    private HttpClient WithToken(string token)
    {
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return _client;
    }

    // -----------------------------------------------------------------------
    // Policy 1: can-edit-quotes (claim-based)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CreateQuote_WithoutQuotesWriteScope_Returns403()
    {
        // reader@example.com has role=viewer — no quotes.write scope in token.
        var token = await LoginAsync("reader@example.com");
        WithToken(token);

        var response = await _client.PostAsJsonAsync(
            "/api/quotes",
            new CreateQuoteRequest { Author = "Seneca", Text = "Dum differtur vita transcurrit." });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "viewer tokens do not carry the quotes.write scope");
    }

    [Fact]
    public async Task CreateQuote_WithQuotesWriteScope_Returns201()
    {
        // demo@example.com has role=writer — token includes scope=quotes.write.
        var token = await LoginAsync("demo@example.com");
        WithToken(token);

        var response = await _client.PostAsJsonAsync(
            "/api/quotes",
            new CreateQuoteRequest { Author = "Seneca", Text = "Dum differtur vita transcurrit." });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // -----------------------------------------------------------------------
    // Policy 2: can-delete-own-quote (custom IAuthorizationRequirement)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DeleteQuote_NotOwner_Returns403()
    {
        // Seed creates quote id=1 owned by demo (writer). reader tries to delete it.
        var token = await LoginAsync("reader@example.com");
        WithToken(token);

        var response = await _client.DeleteAsync("/api/quotes/1");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "a user who did not create the quote must not be able to delete it");
    }

    [Fact]
    public async Task DeleteQuote_Owner_Returns204()
    {
        // demo owns quote id=1 — delete must succeed.
        var token = await LoginAsync("demo@example.com");
        WithToken(token);

        var response = await _client.DeleteAsync("/api/quotes/1");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
