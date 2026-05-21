using QuotesApi.Exceptions;

namespace QuotesApi.Models;

public class Collection
{
    private readonly List<CollectionItem> _items = new();

    public int Id { get; private set; }
    public string Name { get; private set; }
    public string OwnerId { get; private set; }
    
    public IReadOnlyCollection<CollectionItem> Items => _items.AsReadOnly();

    private Collection() { } // For EF Core

    public Collection(string name, string ownerId)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Name cannot be empty.");
            
        if (name.Length < 3 || name.Length > 80)
            throw new DomainException("Name must be between 3 and 80 characters.");

        Name = name;
        OwnerId = ownerId;
    }

    public void AddItem(int quoteId)
    {
        if (_items.Count >= 50)
            throw new DomainException("Collection cannot contain more than 50 items.");

        if (_items.Any(i => i.QuoteId == quoteId))
            throw new DomainException("Quote is already in the collection.");

        _items.Add(new CollectionItem(quoteId, DateTime.UtcNow));
    }

    public void RemoveItem(int quoteId)
    {
        var item = _items.FirstOrDefault(i => i.QuoteId == quoteId);
        if (item == null)
            throw new DomainException("Quote is not in the collection.");

        _items.Remove(item);
    }
}
