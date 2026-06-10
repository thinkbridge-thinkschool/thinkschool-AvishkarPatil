namespace QuotesApi.Application.Queries.Collections;

// A read model is shaped for the SCREEN that consumes it, not for the
// database schema.  This DTO represents the "collection detail page": the
// collection metadata, two pre-computed aggregates (ItemCount,
// LastUpdatedAt), and the flattened list of quotes in display order.
//
// It is NOT a domain entity:
//   - no behaviour
//   - no invariants
//   - no private setters
//   - no relationship navigation
// Just the fields the UI binds to.  EF Core projects directly into this
// shape via .Select(...), so no Collection / CollectionItem / Quote
// entities are materialised on the read path.
public sealed record CollectionDetailReadModel
{
    public int    Id      { get; init; }
    public string Name    { get; init; } = string.Empty;
    public string OwnerId { get; init; } = string.Empty;

    // Denormalised — computed in SQL at query time.  The write side never
    // stores ItemCount; the query computes it from c.Items.Count.  This is
    // exactly the kind of aggregation the read model exists to carry.
    public int ItemCount { get; init; }

    // Denormalised — most-recent AddedAt across all items.  Null when the
    // collection has no items yet.  Useful for sorting collections by
    // recent activity on a dashboard.
    public DateTime? LastUpdatedAt { get; init; }

    // The full list of quotes IN this collection, already joined with the
    // Quotes table.  The UI does NOT need to make a second call to look up
    // author/text per quote — that would be N+1 over the wire.
    public IReadOnlyList<QuoteSummaryReadModel> Quotes { get; init; }
        = Array.Empty<QuoteSummaryReadModel>();
}
