namespace QuotesApi.Services;

/// <summary>
/// Production implementation of <see cref="IClock"/>.
/// Registered as Singleton — it has no state, no dependencies,
/// and DateTimeOffset.UtcNow is thread-safe.
/// </summary>
public class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
