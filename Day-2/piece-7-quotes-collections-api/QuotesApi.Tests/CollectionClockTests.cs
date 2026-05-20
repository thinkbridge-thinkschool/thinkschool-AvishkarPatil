using QuotesApi.Models;
using QuotesApi.Services;

namespace QuotesApi.Tests;

public class CollectionClockTests
{
    [Fact]
    public void AddItem_UsesFakeClock_SoTimestampIsDeterministic()
    {
        // Arrange — freeze time at a known instant
        var fakeClock = new FakeClock
        {
            UtcNow = new DateTimeOffset(2025, 6, 15, 12, 0, 0, TimeSpan.Zero)
        };

        var collection = new Collection("Test Collection", "owner1");

        // Act — pass the fake clock's time instead of DateTime.UtcNow
        collection.AddItem(quoteId: 42, addedAt: fakeClock.UtcNow);

        // Assert — the timestamp is exactly what we set, no flakiness
        var item = Assert.Single(collection.Items);
        Assert.Equal(42, item.QuoteId);
        Assert.Equal(
            new DateTimeOffset(2025, 6, 15, 12, 0, 0, TimeSpan.Zero),
            item.AddedAt);
    }
}
