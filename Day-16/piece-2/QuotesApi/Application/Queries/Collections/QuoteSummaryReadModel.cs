namespace QuotesApi.Application.Queries.Collections;

// A flattened view of "a quote AS IT APPEARS in a collection".  Combines
// fields from two tables (Quotes, CollectionItems) into one DTO.  The UI
// gets exactly what it renders — id, author, text, both timestamps —
// without joining anything on its side.
public sealed record QuoteSummaryReadModel
{
    public int    Id     { get; init; }
    public string Author { get; init; } = string.Empty;
    public string Text   { get; init; } = string.Empty;

    // When this quote was originally authored.  From the Quotes table.
    public DateTime CreatedAt { get; init; }

    // When this quote was added to THIS collection.  From CollectionItems.
    // Different from CreatedAt because a quote can exist long before being
    // added to any particular collection.  The UI sorts by AddedAt.
    public DateTime AddedAt { get; init; }
}
