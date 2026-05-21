namespace QuotesApi.Models;

public sealed class RefreshToken
{
    public int Id { get; private set; }
    public int UserId { get; private set; }
    public string Token { get; private set; } = string.Empty;          // SHA-256 hex of raw token
    public Guid FamilyId { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }
    public string? ReplacedByToken { get; private set; }               // SHA-256 hex of successor token

    private RefreshToken() { }

    public static RefreshToken Create(int userId, string hashedToken, Guid familyId, DateTime expiresAt) =>
        new() { UserId = userId, Token = hashedToken, FamilyId = familyId, ExpiresAt = expiresAt };

    public bool IsValid => RevokedAt is null && ExpiresAt > DateTime.UtcNow;

    public void Revoke(DateTime? at = null) => RevokedAt = at ?? DateTime.UtcNow;

    // Marks this token as consumed and records which token replaced it.
    public void Replace(string newHashedToken)
    {
        RevokedAt = DateTime.UtcNow;
        ReplacedByToken = newHashedToken;
    }
}
