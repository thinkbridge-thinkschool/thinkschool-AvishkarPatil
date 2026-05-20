namespace QuotesApi.Services;

/// <summary>
/// Provides a unique identifier per injection.
/// Registered as Transient — a fresh GUID is generated every time
/// the container resolves this service, proving each consumer
/// gets its own instance.
/// </summary>
public interface IRequestIdProvider
{
    Guid RequestId { get; }
}
