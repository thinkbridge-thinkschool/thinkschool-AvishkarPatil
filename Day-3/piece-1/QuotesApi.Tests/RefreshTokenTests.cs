using System.Net;
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

public class RefreshTokenTests : IClassFixture<RefreshTokenTests.InMemoryFactory>, IDisposable
{
    // -----------------------------------------------------------------------
    // Custom factory: wires an in-memory SQLite DB so tests are fully isolated
    // from the file-based development database.
    // -----------------------------------------------------------------------
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

    public RefreshTokenTests(InMemoryFactory factory)
    {
        _client = factory.CreateClient();
    }

    public void Dispose() => _client.Dispose();

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private async Task<LoginResponse> LoginAsync()
    {
        // DbSeeder creates demo@example.com / P@ssw0rd! on first startup.
        var response = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("demo@example.com", "P@ssw0rd!"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    private Task<HttpResponseMessage> TryRefreshAsync(string rawToken) =>
        _client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest(rawToken));

    // -----------------------------------------------------------------------
    // Tests
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Refresh_ValidToken_ReturnsNewPairAndInvalidatesOld()
    {
        var login = await LoginAsync();

        var refreshResp = await TryRefreshAsync(login.RefreshToken);
        refreshResp.EnsureSuccessStatusCode();
        var refreshed = (await refreshResp.Content.ReadFromJsonAsync<LoginResponse>())!;

        refreshed.AccessToken.Should().NotBe(login.AccessToken);
        refreshed.RefreshToken.Should().NotBe(login.RefreshToken);

        // The original refresh token must now be invalid.
        var retry = await TryRefreshAsync(login.RefreshToken);
        retry.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_TokenReuseAfterRotation_RevokesEntireFamily()
    {
        var login = await LoginAsync();
        var originalToken = login.RefreshToken;

        // First legitimate refresh: original → new.
        var firstRefresh = await TryRefreshAsync(originalToken);
        firstRefresh.EnsureSuccessStatusCode();
        var newPair = (await firstRefresh.Content.ReadFromJsonAsync<LoginResponse>())!;

        // Attacker replays the already-rotated original token.
        var reuseAttempt = await TryRefreshAsync(originalToken);
        reuseAttempt.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "presenting a consumed token must be rejected as a potential theft");

        // The legitimate successor token must now be dead (family was revoked).
        var newTokenAttempt = await TryRefreshAsync(newPair.RefreshToken);
        newTokenAttempt.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the entire token family must be revoked when reuse is detected");
    }

    [Fact]
    public async Task Logout_ValidToken_RevokesAndPreventsSubsequentRefresh()
    {
        var login = await LoginAsync();

        var logoutResp = await _client.PostAsJsonAsync(
            "/api/auth/logout",
            new RefreshTokenRequest(login.RefreshToken));
        logoutResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var refreshAttempt = await TryRefreshAsync(login.RefreshToken);
        refreshAttempt.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
