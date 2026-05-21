using System.Net;
using System.Net.Http.Json;
using QuotesApi.DTOs;
using Quotes.Tests.Integration.Infrastructure;

namespace Quotes.Tests.Integration.Tests;

/// <summary>
/// Happy-path and error-path tests for /api/auth/* endpoints.
/// Each [Fact] gets its own IntegrationTestBase instance → isolated DB.
/// </summary>
public class AuthTests : IntegrationTestBase
{
    // -----------------------------------------------------------------------
    // POST /api/auth/login
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Login_ValidCredentials_Returns200WithTokenPair()
    {
        var response = await Client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("demo@example.com", "P@ssw0rd!"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        body!.AccessToken.Should().NotBeNullOrEmpty();
        body.RefreshToken.Should().NotBeNullOrEmpty();
        body.ExpiresIn.Should().BePositive();
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401()
    {
        var response = await Client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("demo@example.com", "wrong-password"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_UnknownEmail_Returns401()
    {
        var response = await Client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("nobody@example.com", "P@ssw0rd!"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // -----------------------------------------------------------------------
    // POST /api/auth/refresh
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Refresh_ValidToken_Returns200WithRotatedTokenPair()
    {
        var login = await LoginAsync("demo@example.com");

        var response = await Client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshTokenRequest(login.RefreshToken));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        body!.AccessToken.Should().NotBeNullOrEmpty();
        body.RefreshToken.Should().NotBe(login.RefreshToken,
            "rotate-on-use: every successful refresh must issue a brand-new token");
    }

    [Fact]
    public async Task Refresh_InvalidToken_Returns401()
    {
        var response = await Client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshTokenRequest("not-a-real-token"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_ReuseDetected_RevokesFamilyAndReturns401()
    {
        var login = await LoginAsync("demo@example.com");

        // Consume the original token once — this is the legitimate rotation.
        await Client.PostAsJsonAsync("/api/auth/refresh",
            new RefreshTokenRequest(login.RefreshToken));

        // Replay the already-consumed token: family revocation must fire.
        var replay = await Client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshTokenRequest(login.RefreshToken));

        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "reusing a consumed refresh token is a theft signal — entire family is revoked");
    }

    // -----------------------------------------------------------------------
    // POST /api/auth/logout
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Logout_ValidToken_Returns204()
    {
        var login = await LoginAsync("demo@example.com");

        var response = await Client.PostAsJsonAsync(
            "/api/auth/logout",
            new RefreshTokenRequest(login.RefreshToken));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
