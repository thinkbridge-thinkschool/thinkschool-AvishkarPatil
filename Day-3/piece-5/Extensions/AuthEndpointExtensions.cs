using QuotesApi.DTOs;
using QuotesApi.Models;
using QuotesApi.Repositories;
using QuotesApi.Services;

namespace QuotesApi.Extensions;

public static class AuthEndpointExtensions
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/login", async (
            LoginRequest request,
            IUserRepository userRepo,
            ITokenService tokenService,
            IConfiguration config,
            CancellationToken ct) =>
        {
            var user = await userRepo.GetByEmailAsync(request.Email, ct);

            // Constant-time comparison to resist timing attacks: always verify even if user is null.
            var dummyHash = "$2a$11$invalidhashusedtoblindtimingattacksxxxxxxxxxxxxxxxxxxxxxxxxx";
            var passwordValid = user is not null
                ? user.VerifyPassword(request.Password)
                : BCrypt.Net.BCrypt.Verify(request.Password, dummyHash);

            if (!passwordValid)
                return Results.Problem("Invalid email or password.", statusCode: 401);

            var accessToken = tokenService.CreateAccessToken(user!);
            var rawRefresh = tokenService.CreateRefreshToken();
            var hashedRefresh = tokenService.HashToken(rawRefresh);
            var expiresInMinutes = config.GetValue<int>("Jwt:AccessTokenExpiresInMinutes", 15);
            var refreshExpiresInDays = config.GetValue<int>("Jwt:RefreshTokenExpiresInDays", 7);

            var refreshToken = RefreshToken.Create(
                user!.Id,
                hashedRefresh,
                Guid.NewGuid(),
                DateTime.UtcNow.AddDays(refreshExpiresInDays));

            await userRepo.AddRefreshTokenAsync(refreshToken, ct);

            return Results.Ok(new LoginResponse(accessToken, rawRefresh, expiresInMinutes * 60));
        })
        .WithName("Login")
        .WithTags("Auth");

        app.MapPost("/api/auth/refresh", async (
            RefreshTokenRequest request,
            IUserRepository userRepo,
            ITokenService tokenService,
            IConfiguration config,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("Security");
            var hash = tokenService.HashToken(request.Token);
            var stored = await userRepo.GetRefreshTokenByHashAsync(hash, ct);

            if (stored is null)
                return Results.Problem("Invalid refresh token.", statusCode: 401);

            if (stored.RevokedAt is not null)
            {
                // Token was already consumed — this is a reuse attack. Revoke the entire family.
                logger.LogWarning(
                    "Refresh token reuse detected for family {FamilyId}. Revoking entire chain.",
                    stored.FamilyId);
                await userRepo.RevokeTokenFamilyAsync(stored.FamilyId, ct);
                return Results.Problem("Refresh token reuse detected. Please log in again.", statusCode: 401);
            }

            if (!stored.IsValid)
                return Results.Problem("Refresh token has expired.", statusCode: 401);

            var user = await userRepo.GetByIdAsync(stored.UserId, ct);
            if (user is null)
                return Results.Problem("User not found.", statusCode: 401);

            var newAccessToken = tokenService.CreateAccessToken(user);
            var newRawRefresh = tokenService.CreateRefreshToken();
            var newHash = tokenService.HashToken(newRawRefresh);
            var refreshExpiresInDays = config.GetValue<int>("Jwt:RefreshTokenExpiresInDays", 7);
            var expiresInMinutes = config.GetValue<int>("Jwt:AccessTokenExpiresInMinutes", 15);

            var newRefreshToken = RefreshToken.Create(
                stored.UserId,
                newHash,
                stored.FamilyId,
                DateTime.UtcNow.AddDays(refreshExpiresInDays));

            // Mark old token as replaced; EF tracks both changes so AddRefreshTokenAsync saves both.
            stored.Replace(newHash);
            await userRepo.AddRefreshTokenAsync(newRefreshToken, ct);

            return Results.Ok(new LoginResponse(newAccessToken, newRawRefresh, expiresInMinutes * 60));
        })
        .WithName("RefreshToken")
        .WithTags("Auth");

        app.MapPost("/api/auth/logout", async (
            RefreshTokenRequest request,
            IUserRepository userRepo,
            ITokenService tokenService,
            CancellationToken ct) =>
        {
            var hash = tokenService.HashToken(request.Token);
            var stored = await userRepo.GetRefreshTokenByHashAsync(hash, ct);

            if (stored?.IsValid == true)
                await userRepo.RevokeRefreshTokenAsync(stored, ct);

            return Results.NoContent();
        })
        .WithName("Logout")
        .WithTags("Auth");

        return app;
    }
}
