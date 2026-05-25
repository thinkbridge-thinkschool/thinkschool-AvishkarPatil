using QuotesApi.Services;

namespace Quotes.Tests.Integration.Infrastructure;

/// <summary>
/// IClock replacement that tests can freeze or advance.
/// Useful for simulating expired tokens without Thread.Sleep.
/// </summary>
public sealed class FakeClock : IClock
{
    public DateTime UtcNow { get; set; } = DateTime.UtcNow;

    public void AdvanceBy(TimeSpan duration) => UtcNow += duration;
}
