using System.ComponentModel.DataAnnotations;

namespace QuotesApi.Configuration;

public sealed record EntraIdOptions
{
    public const string SectionName = "EntraId";

    [Required]
    public string TenantId { get; init; } = default!;

    [Required]
    public string ClientId { get; init; } = default!;

    [Required]
    public string Audience { get; init; } = default!;
}
