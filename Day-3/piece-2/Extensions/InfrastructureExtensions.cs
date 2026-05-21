using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using QuotesApi.Authorization;
using QuotesApi.Data;
using QuotesApi.Repositories;
using QuotesApi.Services;

namespace QuotesApi.Extensions;

public static class InfrastructureExtensions
{
    private const string InternalScheme = "Internal";
    private const string EntraScheme   = "EntraId";
    private const string MultiScheme   = "MultiAuth";

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseSqlite(
                configuration.GetConnectionString("Default"));
        });

        services.AddScoped<IQuoteRepository, QuoteRepository>();
        services.AddScoped<ICollectionRepository, CollectionRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ITokenService, TokenService>();

        var jwtKey = configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("Jwt:Key is not configured.");

        services
            .AddAuthentication(MultiScheme)
            .AddPolicyScheme(MultiScheme, "JWT – internal or Entra", options =>
            {
                // Peek at the issuer claim without validating the signature,
                // then forward to the scheme that owns that issuer.
                options.ForwardDefaultSelector = ctx =>
                {
                    var auth = ctx.Request.Headers.Authorization.FirstOrDefault();
                    if (auth?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        var raw     = auth["Bearer ".Length..].Trim();
                        var handler = new JwtSecurityTokenHandler();
                        if (handler.CanReadToken(raw))
                        {
                            var jwt = handler.ReadJwtToken(raw);
                            if (jwt.Issuer.Contains("microsoftonline.com",
                                    StringComparison.OrdinalIgnoreCase))
                                return EntraScheme;
                        }
                    }
                    return InternalScheme;
                };
            })
            .AddJwtBearer(InternalScheme, options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer           = true,
                    ValidateAudience         = true,
                    ValidateLifetime         = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer              = configuration["Jwt:Issuer"],
                    ValidAudience            = configuration["Jwt:Audience"],
                    IssuerSigningKey         = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(jwtKey)),
                    ClockSkew                = TimeSpan.Zero
                };
            })
            .AddJwtBearer(EntraScheme, options =>
            {
                options.Authority = $"https://login.microsoftonline.com/{configuration["EntraId:TenantId"]}/v2.0";
                options.Audience  = configuration["EntraId:Audience"];
            });

        services.AddScoped<IAuthorizationHandler, QuoteOwnerHandler>();

        services.AddAuthorization(options =>
        {
            // Policy 1: claim-based — token must carry scope=quotes.write
            options.AddPolicy("can-edit-quotes",
                p => p.RequireClaim("scope", "quotes.write"));

            // Policy 2: custom requirement — authenticated user must own the quote
            options.AddPolicy("can-delete-own-quote",
                p => p.Requirements.Add(new QuoteOwnerRequirement()));
        });

        return services;
    }
}
