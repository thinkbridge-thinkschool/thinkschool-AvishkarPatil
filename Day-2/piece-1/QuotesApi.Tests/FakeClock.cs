using QuotesApi.Services;

namespace QuotesApi.Tests;

/// <summary>
/// Test double — returns whatever time you set.
/// This is the whole reason IClock exists.
/// </summary>
public class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; }
        = new DateTimeOffset(2025, 6, 15, 12, 0, 0, TimeSpan.Zero);
}
