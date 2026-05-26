using System.ComponentModel.DataAnnotations;

namespace QuotesApi.Configuration;

public sealed record JwtOptions
{
    public const string SectionName = "Jwt";

    [Required, MinLength(32)]
    public string Key { get; init; } = default!;

    [Required]
    public string Issuer { get; init; } = default!;

    [Required]
    public string Audience { get; init; } = default!;

    [Range(1, 1440)]
    public int AccessTokenExpiresInMinutes { get; init; } = 15;

    [Range(1, 365)]
    public int RefreshTokenExpiresInDays { get; init; } = 7;

    public TimeSpan AccessTokenLifetime => TimeSpan.FromMinutes(AccessTokenExpiresInMinutes);
    public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(RefreshTokenExpiresInDays);
}
