using QuotesApi.Exceptions;
using QuotesApi.Models;

namespace Quotes.Tests.Unit;

public class QuoteTests
{
    // ── Quote.Create ─────────────────────────────────────────────────────────

    [Fact]
    public void Create_WithValidInputs_ReturnsTrimmedQuote()
    {
        // Arrange
        var author = "  Marcus Aurelius  ";
        var text   = "  The impediment to action advances action.  ";

        // Act
        var quote = Quote.Create(author, text);

        // Assert
        quote.Author.Should().Be("Marcus Aurelius");
        quote.Text.Should().Be("The impediment to action advances action.");
        quote.IsDeleted.Should().BeFalse();
        quote.OwnerId.Should().BeNull();
    }

    [Fact]
    public void Create_WithOwnerId_SetsOwnerIdOnResult()
    {
        // Arrange / Act
        var quote = Quote.Create("Seneca", "Per aspera ad astra.", ownerId: 42);

        // Assert
        quote.OwnerId.Should().Be(42);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankAuthor_ThrowsDomainException(string? author)
    {
        // Arrange
        var act = () => Quote.Create(author!, "Some valid text");

        // Assert
        act.Should().Throw<DomainException>()
           .WithMessage("Author must be between 1 and 200 characters.");
    }

    [Fact]
    public void Create_WithAuthorExceeding200Characters_ThrowsDomainException()
    {
        // Arrange
        var tooLongAuthor = new string('A', 201);
        var act = () => Quote.Create(tooLongAuthor, "Some valid text");

        // Assert
        act.Should().Throw<DomainException>()
           .WithMessage("Author must be between 1 and 200 characters.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankText_ThrowsDomainException(string? text)
    {
        // Arrange
        var act = () => Quote.Create("Author", text!);

        // Assert
        act.Should().Throw<DomainException>()
           .WithMessage("Text must be between 1 and 1000 characters.");
    }

    [Fact]
    public void Create_WithTextExceeding1000Characters_ThrowsDomainException()
    {
        // Arrange
        var tooLongText = new string('x', 1001);
        var act = () => Quote.Create("Author", tooLongText);

        // Assert
        act.Should().Throw<DomainException>()
           .WithMessage("Text must be between 1 and 1000 characters.");
    }

    [Fact]
    public void Create_WithAuthorExactly200Characters_ReturnsQuote()
    {
        // Arrange
        var exactAuthor = new string('A', 200);

        // Act
        var quote = Quote.Create(exactAuthor, "Some text");

        // Assert
        quote.Author.Should().HaveLength(200);
    }

    [Fact]
    public void Create_WithTextExactly1000Characters_ReturnsQuote()
    {
        // Arrange
        var exactText = new string('x', 1000);

        // Act
        var quote = Quote.Create("Author", exactText);

        // Assert
        quote.Text.Should().HaveLength(1000);
    }

    // ── Quote.SoftDelete ─────────────────────────────────────────────────────

    [Fact]
    public void SoftDelete_OnActiveQuote_SetsIsDeletedToTrue()
    {
        // Arrange
        var quote = Quote.Create("Epictetus", "Make the best use of what is in your power.");

        // Act
        quote.SoftDelete();

        // Assert
        quote.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public void SoftDelete_CalledTwice_RemainsDeleted()
    {
        // Arrange
        var quote = Quote.Create("Epictetus", "He is a wise man who does not grieve.");
        quote.SoftDelete();

        // Act
        quote.SoftDelete();

        // Assert
        quote.IsDeleted.Should().BeTrue();
    }
}
