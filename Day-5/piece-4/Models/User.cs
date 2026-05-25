using QuotesApi.Exceptions;

namespace QuotesApi.Models;

public sealed class User
{
    public int Id { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public string Role { get; private set; } = "viewer";

    private User() { }

    public static User Create(string email, string plainPassword, string role = "viewer")
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 200)
            throw new DomainException("Email must be 1–200 characters.");

        if (string.IsNullOrWhiteSpace(plainPassword))
            throw new DomainException("Password is required.");

        if (role != "writer" && role != "viewer")
            throw new DomainException("Role must be 'writer' or 'viewer'.");

        return new User
        {
            Email = email.Trim().ToLowerInvariant(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(plainPassword),
            Role = role
        };
    }

    public bool VerifyPassword(string plainPassword) =>
        BCrypt.Net.BCrypt.Verify(plainPassword, PasswordHash);
}
