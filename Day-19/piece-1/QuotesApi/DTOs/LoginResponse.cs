namespace QuotesApi.DTOs;

public sealed record LoginResponse(
    string AccessToken,
    string RefreshToken,
    int ExpiresIn);
