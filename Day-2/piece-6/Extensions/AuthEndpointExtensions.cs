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

            // Use constant-time comparison to resist timing attacks: always verify even if user is null
            var dummyHash = "$2a$11$invalidhashusedtoblindtimingattacksxxxxxxxxxxxxxxxxxxxxxxxxx";
            var passwordValid = user is not null
                ? user.VerifyPassword(request.Password)
                : BCrypt.Net.BCrypt.Verify(request.Password, dummyHash);

            if (!passwordValid)
                return Results.Problem("Invalid email or password.", statusCode: 401);

            var accessToken = tokenService.CreateAccessToken(user!);
            var refreshTokenStr = tokenService.CreateRefreshToken();
            var expiresInMinutes = config.GetValue<int>("Jwt:AccessTokenExpiresInMinutes", 15);
            var refreshExpiresInDays = config.GetValue<int>("Jwt:RefreshTokenExpiresInDays", 7);

            var refreshToken = RefreshToken.Create(
                user!.Id,
                refreshTokenStr,
                DateTime.UtcNow.AddDays(refreshExpiresInDays));

            await userRepo.AddRefreshTokenAsync(refreshToken, ct);

            return Results.Ok(new LoginResponse(accessToken, refreshTokenStr, expiresInMinutes * 60));
        })
        .WithName("Login")
        .WithTags("Auth");

        return app;
    }
}
