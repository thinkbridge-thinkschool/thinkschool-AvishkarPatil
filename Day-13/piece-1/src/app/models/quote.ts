// Domain models — mirror the read model returned by the Day-12 Week-1 API
// endpoint  GET /api/collections/{id}  (CollectionDetailReadModel +
// QuoteSummaryReadModel).  These shapes are what the API actually returns,
// so httpResource<CollectionDetail> deserialises straight into them.

// One quote as it appears inside a collection (Quotes ⋈ CollectionItems).
export interface Quote {
  id:        number;
  author:    string;
  text:      string;
  createdAt: string;   // when the quote was authored (Quotes table)
  addedAt:   string;   // when it was added to THIS collection (CollectionItems)
}

// The full collection-detail payload — exactly the JSON the Week-1 API returns.
export interface CollectionDetail {
  id:            number;
  name:          string;
  ownerId:       string;
  itemCount:     number;        // server-side COUNT(*)
  lastUpdatedAt: string | null; // server-side MAX(AddedAt); null when empty
  quotes:        Quote[];
}
