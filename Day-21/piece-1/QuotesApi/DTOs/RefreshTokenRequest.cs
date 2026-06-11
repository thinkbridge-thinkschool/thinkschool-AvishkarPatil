using System.ComponentModel.DataAnnotations;

namespace QuotesApi.DTOs;

public sealed record RefreshTokenRequest([Required] string Token);
