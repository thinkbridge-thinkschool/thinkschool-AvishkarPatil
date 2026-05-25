using FluentAssertions;
using QuotesApi.Exceptions;
using QuotesApi.Models;

namespace QuotesApi.Tests;

public class CollectionDomainTests
{
    [Fact]
    public void Constructor_EmptyName_ThrowsDomainException()
    {
        var act = () => new Collection("", "owner1");
        act.Should().Throw<DomainException>().WithMessage("Name cannot be empty.");
    }

    [Fact]
    public void Constructor_NameExceeds80Chars_ThrowsDomainException()
    {
        var act = () => new Collection(new string('a', 81), "owner1");
        act.Should().Throw<DomainException>().WithMessage("Name must be between 3 and 80 characters.");
    }

    [Fact]
    public void AddItem_51stItem_ThrowsDomainException()
    {
        var collection = new Collection("My Collection", "owner1");
        for (var i = 1; i <= 50; i++) collection.AddItem(i);

        var act = () => collection.AddItem(51);
        act.Should().Throw<DomainException>().WithMessage("Collection cannot contain more than 50 items.");
    }

    [Fact]
    public void AddItem_DuplicateQuoteId_ThrowsDomainException()
    {
        var collection = new Collection("My Collection", "owner1");
        collection.AddItem(1);

        var act = () => collection.AddItem(1);
        act.Should().Throw<DomainException>().WithMessage("Quote is already in the collection.");
    }

    [Fact]
    public void RemoveItem_NonExistentQuoteId_ThrowsDomainException()
    {
        var collection = new Collection("My Collection", "owner1");

        var act = () => collection.RemoveItem(99);
        act.Should().Throw<DomainException>().WithMessage("Quote is not in the collection.");
    }

    [Fact]
    public void AddThenRemove_LeavesZeroItems()
    {
        var collection = new Collection("My Collection", "owner1");
        collection.AddItem(1);
        collection.RemoveItem(1);

        collection.Items.Should().BeEmpty();
    }
}
