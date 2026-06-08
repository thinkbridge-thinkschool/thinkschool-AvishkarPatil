namespace QuotesApi.BackgroundJobs;

public sealed record QuoteAuditItem(
    int    QuoteId,
    int?   UserId,
    string Author,
    DateTimeOffset CreatedAt);
