namespace QuotesApi.Services;

/// <summary>
/// Abstraction over the system clock.
/// Registered as Singleton — genuinely stateless, thread-safe,
/// and used cross-cutting throughout the app.
/// In tests, swap with a fake that returns a fixed time.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
