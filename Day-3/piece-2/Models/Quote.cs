using QuotesApi.Exceptions;

namespace QuotesApi.Models;

public class Quote
{
    public int Id { get; private set; }
    public string Author { get; private set; } = string.Empty;
    public string Text { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; private set; }
    public bool IsDeleted { get; private set; }
    public int? OwnerId { get; private set; }

    private Quote() { } // For EF Core

    public static Quote Create(string author, string text, int? ownerId = null)
    {
        if (string.IsNullOrWhiteSpace(author) || author.Length > 200)
            throw new DomainException("Author must be between 1 and 200 characters.");

        if (string.IsNullOrWhiteSpace(text) || text.Length > 1000)
            throw new DomainException("Text must be between 1 and 1000 characters.");

        return new Quote
        {
            Author = author.Trim(),
            Text = text.Trim(),
            CreatedAt = DateTime.UtcNow,
            OwnerId = ownerId
        };
    }

    public void SoftDelete()
    {
        IsDeleted = true;
    }
}
