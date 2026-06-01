// Domain model — mirrors the read model returned by the Day-12 API
// (CollectionDetailReadModel / QuoteSummaryReadModel).
// Used as the value type inside signals — keeping the type here means
// both the service and the component import from one place.

export interface Quote {
  id:        number;
  author:    string;
  text:      string;
  createdAt: string;
  addedAt:   string;
}
