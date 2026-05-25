using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Data;
using QuotesApi.DTOs;
using QuotesApi.Models;
using Quotes.Tests.Integration.Infrastructure;

namespace Quotes.Tests.Integration.Tests;

/// <summary>
/// Happy-path and error-path tests for /api/auth/* endpoints.
/// Each [Fact] gets its own IntegrationTestBase instance → isolated DB.
/// </summary>
[Collection("SqlServer")]
public class AuthTests : IntegrationTestBase
{
    public AuthTests(SqlServerContainerFixture fixture) : base(fixture) { }

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

    [Fact]
    public async Task Refresh_ExpiredToken_Returns401()
    {
        // Arrange — insert a token whose ExpiresAt is yesterday so IsValid = false
        // but RevokedAt is null, exercising the "expired but not revoked" branch.
        const string rawToken = "integration-test-expired-refresh-token";
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)))
            .ToLowerInvariant();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = db.Users.First(u => u.Email == "demo@example.com").Id;

        var expiredToken = RefreshToken.Create(
            userId, hash, Guid.NewGuid(), DateTime.UtcNow.AddDays(-1));
        db.RefreshTokens.Add(expiredToken);
        await db.SaveChangesAsync();

        // Act
        var response = await Client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshTokenRequest(rawToken));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "a non-revoked but time-expired refresh token must be rejected");
    }

    [Fact]
    public async Task Refresh_UserDeletedAfterTokenIssuance_Returns401()
    {
        // Arrange — login to get a valid refresh token, then delete the user.
        // No FK constraint between RefreshTokens.UserId and Users, so the token
        // remains in the DB as an orphan, exercising the "user not found" guard.
        var login = await LoginAsync("demo@example.com");

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = db.Users.Single(u => u.Email == "demo@example.com");
        db.Users.Remove(user);
        await db.SaveChangesAsync();

        // Act
        var response = await Client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshTokenRequest(login.RefreshToken));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "a valid token for a deleted user must be rejected");
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
