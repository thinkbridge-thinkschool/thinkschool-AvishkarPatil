using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Configuration;
using QuotesApi.Models;
using QuotesApi.Services;

namespace Quotes.Tests.Unit;

public class TokenServiceTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IConfiguration BuildConfig(int expiresInMinutes = 15) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"]                        = "unit-test-key-32bytes-minimum-xx!!",
                ["Jwt:Issuer"]                     = "TestIssuer",
                ["Jwt:Audience"]                   = "TestAudience",
                ["Jwt:AccessTokenExpiresInMinutes"] = expiresInMinutes.ToString()
            })
            .Build();

    private static TokenService BuildSut(IClock clock, int expiresInMinutes = 15) =>
        new(BuildConfig(expiresInMinutes), clock);

    // ── CreateAccessToken ─────────────────────────────────────────────────────

    [Fact]
    public void CreateAccessToken_ForWriterUser_ContainsScopeWriteClaim()
    {
        // Arrange
        var fakeClock = Substitute.For<IClock>();
        fakeClock.UtcNow.Returns(DateTime.UtcNow);
        var sut  = BuildSut(fakeClock);
        var user = User.Create("writer@example.com", "P@ssw0rd!", role: "writer");

        // Act
        var token = sut.CreateAccessToken(user);

        // Assert
        var jwt    = new JwtSecurityTokenHandler().ReadJwtToken(token);
        var scopes = jwt.Claims.Where(c => c.Type == "scope").Select(c => c.Value);
        scopes.Should().Contain("quotes.write");
    }

    [Fact]
    public void CreateAccessToken_ForViewerUser_DoesNotContainScopeWriteClaim()
    {
        // Arrange
        var fakeClock = Substitute.For<IClock>();
        fakeClock.UtcNow.Returns(DateTime.UtcNow);
        var sut  = BuildSut(fakeClock);
        var user = User.Create("viewer@example.com", "P@ssw0rd!", role: "viewer");

        // Act
        var token = sut.CreateAccessToken(user);

        // Assert
        var jwt    = new JwtSecurityTokenHandler().ReadJwtToken(token);
        var scopes = jwt.Claims.Where(c => c.Type == "scope").Select(c => c.Value);
        scopes.Should().NotContain("quotes.write");
    }

    [Fact]
    public void CreateAccessToken_WithFakeClock_ExpiresAtExpectedTime()
    {
        // Arrange — pin the clock to a known instant
        var fixedNow  = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var fakeClock = Substitute.For<IClock>();
        fakeClock.UtcNow.Returns(fixedNow);

        var sut  = BuildSut(fakeClock, expiresInMinutes: 30);
        var user = User.Create("timed@example.com", "P@ssw0rd!");

        // Act
        var token = sut.CreateAccessToken(user);

        // Assert — JWT exp claim must equal fixedNow + 30 min (within 1 s for integer rounding)
        var jwt         = new JwtSecurityTokenHandler().ReadJwtToken(token);
        var expectedExp = fixedNow.AddMinutes(30);
        jwt.ValidTo.Should().BeCloseTo(expectedExp, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void CreateAccessToken_WithKeyShorterThan32Bytes_ThrowsInvalidOperationException()
    {
        // Arrange — key is only 16 bytes
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"]      = "short-key",
                ["Jwt:Issuer"]   = "TestIssuer",
                ["Jwt:Audience"] = "TestAudience"
            })
            .Build();

        var fakeClock = Substitute.For<IClock>();
        fakeClock.UtcNow.Returns(DateTime.UtcNow);
        var sut  = new TokenService(config, fakeClock);
        var user = User.Create("u@example.com", "P@ssw0rd!");

        // Act
        var act = () => sut.CreateAccessToken(user);

        // Assert
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*256 bits*");
    }

    [Fact]
    public void CreateAccessToken_ContainsSubAndEmailClaims()
    {
        // Arrange
        var fakeClock = Substitute.For<IClock>();
        fakeClock.UtcNow.Returns(DateTime.UtcNow);
        var sut  = BuildSut(fakeClock);
        var user = User.Create("claims@example.com", "P@ssw0rd!");

        // Act
        var token = sut.CreateAccessToken(user);

        // Assert
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Email
                                      && c.Value == "claims@example.com");
        jwt.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Sub);
    }

    // ── HashToken ─────────────────────────────────────────────────────────────

    [Fact]
    public void HashToken_SameInput_ProducesSameHash()
    {
        // Arrange
        var fakeClock = Substitute.For<IClock>();
        fakeClock.UtcNow.Returns(DateTime.UtcNow);
        var sut   = BuildSut(fakeClock);
        var input = "some-raw-refresh-token";

        // Act
        var hash1 = sut.HashToken(input);
        var hash2 = sut.HashToken(input);

        // Assert
        hash1.Should().Be(hash2);
    }

    [Fact]
    public void HashToken_DifferentInputs_ProduceDifferentHashes()
    {
        // Arrange
        var fakeClock = Substitute.For<IClock>();
        fakeClock.UtcNow.Returns(DateTime.UtcNow);
        var sut = BuildSut(fakeClock);

        // Act
        var hash1 = sut.HashToken("token-one");
        var hash2 = sut.HashToken("token-two");

        // Assert
        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void HashToken_OutputIsLowercaseHex()
    {
        // Arrange
        var fakeClock = Substitute.For<IClock>();
        fakeClock.UtcNow.Returns(DateTime.UtcNow);
        var sut = BuildSut(fakeClock);

        // Act
        var hash = sut.HashToken("any-token");

        // Assert — SHA-256 hex is 64 lowercase hex chars
        hash.Should().HaveLength(64)
            .And.MatchRegex("^[0-9a-f]+$");
    }

    // ── CreateRefreshToken ────────────────────────────────────────────────────

    [Fact]
    public void CreateRefreshToken_ReturnsNonEmptyBase64String()
    {
        // Arrange
        var fakeClock = Substitute.For<IClock>();
        fakeClock.UtcNow.Returns(DateTime.UtcNow);
        var sut = BuildSut(fakeClock);

        // Act
        var raw = sut.CreateRefreshToken();

        // Assert
        raw.Should().NotBeNullOrEmpty();
        var bytes = Convert.FromBase64String(raw);   // throws if not valid Base64
        bytes.Should().HaveCount(64);                // 64 random bytes → 64-byte array
    }

    [Fact]
    public void CreateRefreshToken_CalledTwice_ReturnsDifferentValues()
    {
        // Arrange
        var fakeClock = Substitute.For<IClock>();
        fakeClock.UtcNow.Returns(DateTime.UtcNow);
        var sut = BuildSut(fakeClock);

        // Act
        var first  = sut.CreateRefreshToken();
        var second = sut.CreateRefreshToken();

        // Assert — each call uses a fresh CSPRNG → tokens must differ
        first.Should().NotBe(second);
    }
}
