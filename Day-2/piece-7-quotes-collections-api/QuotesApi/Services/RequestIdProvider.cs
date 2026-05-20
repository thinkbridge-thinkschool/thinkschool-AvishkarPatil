namespace QuotesApi.Services;

/// <summary>
/// Transient implementation — a new instance (with a new Guid)
/// is created every time it is injected. Compare this with Scoped
/// (one per request) and Singleton (one for the app's life).
/// </summary>
public class RequestIdProvider : IRequestIdProvider
{
    public Guid RequestId { get; } = Guid.NewGuid();
}
