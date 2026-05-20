namespace QuotesApi.Models;

public sealed class RefreshToken
{
    public int Id { get; private set; }
    public int UserId { get; private set; }
    public string Token { get; private set; } = string.Empty;
    public DateTime ExpiresAt { get; private set; }
    public bool IsRevoked { get; private set; }

    private RefreshToken() { }

    public static RefreshToken Create(int userId, string token, DateTime expiresAt) =>
        new() { UserId = userId, Token = token, ExpiresAt = expiresAt };

    public bool IsValid => !IsRevoked && ExpiresAt > DateTime.UtcNow;
}
