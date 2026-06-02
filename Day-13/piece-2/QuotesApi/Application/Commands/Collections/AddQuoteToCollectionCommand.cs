namespace QuotesApi.Application.Commands.Collections;

// ActingUserId is the JWT subject — captured at the endpoint and passed in
// so authorisation can live in the handler instead of being scattered
// around HttpContext.User calls.
public sealed record AddQuoteToCollectionCommand(
    int    CollectionId,
    int    QuoteId,
    string ActingUserId);
