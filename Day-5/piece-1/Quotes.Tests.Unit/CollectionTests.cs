using QuotesApi.Exceptions;
using QuotesApi.Models;

namespace Quotes.Tests.Unit;

public class CollectionTests
{
    // ── Collection constructor ────────────────────────────────────────────────

    [Fact]
    public void Constructor_WithValidInputs_SetsNameAndOwner()
    {
        // Arrange / Act
        var collection = new Collection("My Favourites", "owner-abc");

        // Assert
        collection.Name.Should().Be("My Favourites");
        collection.OwnerId.Should().Be("owner-abc");
        collection.Items.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithBlankName_ThrowsDomainException(string? name)
    {
        // Arrange
        var act = () => new Collection(name!, "owner-1");

        // Assert
        act.Should().Throw<DomainException>()
           .WithMessage("Name cannot be empty.");
    }

    [Fact]
    public void Constructor_WithNameShorterThan3Characters_ThrowsDomainException()
    {
        // Arrange
        var act = () => new Collection("AB", "owner-1");

        // Assert
        act.Should().Throw<DomainException>()
           .WithMessage("Name must be between 3 and 80 characters.");
    }

    [Fact]
    public void Constructor_WithNameLongerThan80Characters_ThrowsDomainException()
    {
        // Arrange
        var tooLong = new string('X', 81);
        var act = () => new Collection(tooLong, "owner-1");

        // Assert
        act.Should().Throw<DomainException>()
           .WithMessage("Name must be between 3 and 80 characters.");
    }

    [Fact]
    public void Constructor_WithNameExactly3Characters_CreatesCollection()
    {
        // Arrange / Act
        var collection = new Collection("ABC", "owner-1");

        // Assert
        collection.Name.Should().Be("ABC");
    }

    [Fact]
    public void Constructor_WithNameExactly80Characters_CreatesCollection()
    {
        // Arrange
        var exactName = new string('X', 80);

        // Act
        var collection = new Collection(exactName, "owner-1");

        // Assert
        collection.Name.Should().HaveLength(80);
    }

    // ── Collection.AddItem ────────────────────────────────────────────────────

    [Fact]
    public void AddItem_WithNewQuoteId_IncreasesItemCount()
    {
        // Arrange
        var collection = new Collection("Reading List", "owner-1");

        // Act
        collection.AddItem(quoteId: 7);

        // Assert
        collection.Items.Should().HaveCount(1);
        collection.Items.Single().QuoteId.Should().Be(7);
    }

    [Fact]
    public void AddItem_WhenCollectionIsAtCapacity_ThrowsDomainException()
    {
        // Arrange
        var collection = new Collection("Big List", "owner-1");
        for (var i = 1; i <= 50; i++)
            collection.AddItem(i);

        // Act
        var act = () => collection.AddItem(quoteId: 999);

        // Assert
        act.Should().Throw<DomainException>()
           .WithMessage("Collection cannot contain more than 50 items.");
    }

    [Fact]
    public void AddItem_WithDuplicateQuoteId_ThrowsDomainException()
    {
        // Arrange
        var collection = new Collection("Reading List", "owner-1");
        collection.AddItem(quoteId: 5);

        // Act
        var act = () => collection.AddItem(quoteId: 5);

        // Assert
        act.Should().Throw<DomainException>()
           .WithMessage("Quote is already in the collection.");
    }

    // ── Collection.RemoveItem ─────────────────────────────────────────────────

    [Fact]
    public void RemoveItem_WithExistingQuoteId_DecreasesItemCount()
    {
        // Arrange
        var collection = new Collection("Reading List", "owner-1");
        collection.AddItem(quoteId: 3);
        collection.AddItem(quoteId: 4);

        // Act
        collection.RemoveItem(quoteId: 3);

        // Assert
        collection.Items.Should().HaveCount(1);
        collection.Items.Single().QuoteId.Should().Be(4);
    }

    [Fact]
    public void RemoveItem_WithAbsentQuoteId_ThrowsDomainException()
    {
        // Arrange
        var collection = new Collection("Reading List", "owner-1");

        // Act
        var act = () => collection.RemoveItem(quoteId: 99);

        // Assert
        act.Should().Throw<DomainException>()
           .WithMessage("Quote is not in the collection.");
    }
}
