using QuotesApi.Exceptions;
using QuotesApi.Models;

namespace Quotes.Tests.Unit;

public class UserTests
{
    // ── User.Create ───────────────────────────────────────────────────────────

    [Fact]
    public void Create_WithValidWriterCredentials_ReturnsUserWithWriterRole()
    {
        // Arrange / Act
        var user = User.Create("Writer@Example.COM", "P@ssw0rd!", role: "writer");

        // Assert
        user.Role.Should().Be("writer");
    }

    [Fact]
    public void Create_WithValidCredentials_NormalizesEmailToLowercase()
    {
        // Arrange / Act
        var user = User.Create("USER@EXAMPLE.COM", "P@ssw0rd!");

        // Assert
        user.Email.Should().Be("user@example.com");
    }

    [Fact]
    public void Create_WithValidCredentials_HashesPasswordNotStoringPlaintext()
    {
        // Arrange
        const string password = "P@ssw0rd!";

        // Act
        var user = User.Create("test@example.com", password);

        // Assert
        user.PasswordHash.Should().NotBe(password);
        user.PasswordHash.Should().StartWith("$2");   // BCrypt hash prefix
    }

    [Fact]
    public void Create_DefaultRole_IsViewer()
    {
        // Arrange / Act
        var user = User.Create("reader@example.com", "P@ssw0rd!");

        // Assert
        user.Role.Should().Be("viewer");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankEmail_ThrowsDomainException(string? email)
    {
        // Arrange
        var act = () => User.Create(email!, "P@ssw0rd!");

        // Assert
        act.Should().Throw<DomainException>()
           .WithMessage("Email must be 1–200 characters.");
    }

    [Fact]
    public void Create_WithEmailExceeding200Characters_ThrowsDomainException()
    {
        // Arrange
        var tooLong = new string('a', 195) + "@x.com";   // > 200 chars
        var act = () => User.Create(tooLong, "P@ssw0rd!");

        // Assert
        act.Should().Throw<DomainException>()
           .WithMessage("Email must be 1–200 characters.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankPassword_ThrowsDomainException(string? password)
    {
        // Arrange
        var act = () => User.Create("user@example.com", password!);

        // Assert
        act.Should().Throw<DomainException>()
           .WithMessage("Password is required.");
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("superuser")]
    [InlineData("VIEWER")]          // case-sensitive guard
    public void Create_WithInvalidRole_ThrowsDomainException(string role)
    {
        // Arrange
        var act = () => User.Create("user@example.com", "P@ssw0rd!", role);

        // Assert
        act.Should().Throw<DomainException>()
           .WithMessage("Role must be 'writer' or 'viewer'.");
    }

    // ── User.VerifyPassword ───────────────────────────────────────────────────

    [Fact]
    public void VerifyPassword_WithCorrectPassword_ReturnsTrue()
    {
        // Arrange
        const string password = "CorrectHorseBatteryStaple";
        var user = User.Create("check@example.com", password);

        // Act
        var result = user.VerifyPassword(password);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void VerifyPassword_WithWrongPassword_ReturnsFalse()
    {
        // Arrange
        var user = User.Create("check@example.com", "RightPassword");

        // Act
        var result = user.VerifyPassword("WrongPassword");

        // Assert
        result.Should().BeFalse();
    }
}
