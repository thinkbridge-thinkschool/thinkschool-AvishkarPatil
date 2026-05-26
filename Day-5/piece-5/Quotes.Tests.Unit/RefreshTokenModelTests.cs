using QuotesApi.Models;

namespace Quotes.Tests.Unit;

public class RefreshTokenModelTests
{
    // ── RefreshToken.Create ───────────────────────────────────────────────────

    [Fact]
    public void Create_WithValidParameters_SetsAllProperties()
    {
        // Arrange
        var userId     = 7;
        var hash       = "abc123";
        var familyId   = Guid.NewGuid();
        var expiresAt  = DateTime.UtcNow.AddDays(7);

        // Act
        var token = RefreshToken.Create(userId, hash, familyId, expiresAt);

        // Assert
        token.UserId.Should().Be(userId);
        token.Token.Should().Be(hash);
        token.FamilyId.Should().Be(familyId);
        token.ExpiresAt.Should().BeCloseTo(expiresAt, TimeSpan.FromMilliseconds(10));
        token.RevokedAt.Should().BeNull();
        token.ReplacedByToken.Should().BeNull();
    }

    // ── RefreshToken.IsValid ──────────────────────────────────────────────────

    [Fact]
    public void IsValid_WhenNotExpiredAndNotRevoked_ReturnsTrue()
    {
        // Arrange
        var token = RefreshToken.Create(1, "hash", Guid.NewGuid(), DateTime.UtcNow.AddHours(1));

        // Act / Assert
        token.IsValid.Should().BeTrue();
    }

    [Fact]
    public void IsValid_WhenExpired_ReturnsFalse()
    {
        // Arrange — expiresAt is in the past
        var token = RefreshToken.Create(1, "hash", Guid.NewGuid(), DateTime.UtcNow.AddSeconds(-1));

        // Act / Assert
        token.IsValid.Should().BeFalse();
    }

    [Fact]
    public void IsValid_WhenRevoked_ReturnsFalse()
    {
        // Arrange
        var token = RefreshToken.Create(1, "hash", Guid.NewGuid(), DateTime.UtcNow.AddHours(1));
        token.Revoke();

        // Act / Assert
        token.IsValid.Should().BeFalse();
    }

    // ── RefreshToken.Revoke ───────────────────────────────────────────────────

    [Fact]
    public void Revoke_WithNoArgument_SetsRevokedAtToApproximatelyNow()
    {
        // Arrange
        var token = RefreshToken.Create(1, "hash", Guid.NewGuid(), DateTime.UtcNow.AddHours(1));
        var before = DateTime.UtcNow;

        // Act
        token.Revoke();

        // Assert
        token.RevokedAt.Should().NotBeNull();
        token.RevokedAt!.Value.Should().BeOnOrAfter(before)
            .And.BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Revoke_WithExplicitDateTime_UsesProvidedTime()
    {
        // Arrange
        var revokeTime = new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc);
        var token = RefreshToken.Create(1, "hash", Guid.NewGuid(), DateTime.UtcNow.AddHours(1));

        // Act
        token.Revoke(revokeTime);

        // Assert
        token.RevokedAt.Should().Be(revokeTime);
    }

    // ── RefreshToken.Replace ──────────────────────────────────────────────────

    [Fact]
    public void Replace_WhenCalled_SetsReplacedByTokenToSuccessorHash()
    {
        // Arrange
        var token = RefreshToken.Create(1, "old-hash", Guid.NewGuid(), DateTime.UtcNow.AddHours(1));

        // Act
        token.Replace("new-hash");

        // Assert
        token.ReplacedByToken.Should().Be("new-hash");
    }

    [Fact]
    public void Replace_WhenCalled_SetsRevokedAtMakingTokenInvalid()
    {
        // Arrange
        var token = RefreshToken.Create(1, "old-hash", Guid.NewGuid(), DateTime.UtcNow.AddHours(1));
        token.IsValid.Should().BeTrue("precondition: token was valid before replace");

        // Act
        token.Replace("new-hash");

        // Assert — IsValid = RevokedAt is null && ...; Replace sets RevokedAt
        token.RevokedAt.Should().NotBeNull();
        token.IsValid.Should().BeFalse();
    }

    // ── Reuse-detection scenario ──────────────────────────────────────────────

    [Fact]
    public void ReuseDetection_ConsumedToken_HasNonNullRevokedAt()
    {
        // Simulate what the /api/auth/refresh endpoint does:
        //   stored.Replace(newHash) → old token is consumed → reuse check triggers.

        // Arrange
        var originalToken = RefreshToken.Create(1, "hash-A", Guid.NewGuid(), DateTime.UtcNow.AddDays(7));
        originalToken.IsValid.Should().BeTrue();

        // Act — first legitimate rotation
        originalToken.Replace("hash-B");

        // Assert — the endpoint checks `stored.RevokedAt is not null` to detect reuse
        originalToken.RevokedAt.Should().NotBeNull(
            "a replaced token must be revoked so a second presentation is flagged as reuse");
        originalToken.ReplacedByToken.Should().Be("hash-B");
    }
}
