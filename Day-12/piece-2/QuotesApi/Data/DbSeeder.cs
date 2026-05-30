using Microsoft.EntityFrameworkCore;
using QuotesApi.Models;

namespace QuotesApi.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        if (db.Users.Any())
            return;

        // dev seed only — override credentials via env/secrets before exposing publicly
        var writer = User.Create("demo@example.com", "P@ssw0rd!", role: "writer");
        var viewer = User.Create("reader@example.com", "P@ssw0rd!", role: "viewer");
        db.Users.AddRange(writer, viewer);
        await db.SaveChangesAsync();

        // Seed one quote owned by the writer so delete-own-quote tests have a target.
        var quote = Quote.Create("Marcus Aurelius", "The impediment to action advances action.", writer.Id);
        db.Quotes.Add(quote);
        await db.SaveChangesAsync();
    }

    // ── Day-11 perf seed: enough rows for an N+1 to actually hurt ──────────
    // 500 quotes + 5 collections × 20 items = 100 CollectionItems.
    // A request to /api/collections/1/quotes/slow does 1 + 20 = 21 round-trips.
    // Idempotent — checks counts and tops up only what's missing.
    private static readonly string[] PerfAuthors =
    [
        "Marcus Aurelius", "Seneca", "Epictetus", "Aristotle", "Plato",
        "Socrates", "Nietzsche", "Immanuel Kant", "René Descartes", "Voltaire",
        "Albert Einstein", "Feynman", "Carl Sagan", "Hawking", "Marie Curie",
        "Tolstoy", "Dostoevsky", "Victor Hugo", "Hemingway", "Orwell",
    ];

    private static readonly string[] PerfTemplates =
    [
        "The {0} of the wise is the {1} of all men.",
        "In the middle of {0} lies {1}.",
        "To know {0} is to know {1}.",
        "The measure of a man is what he does with {0}.",
        "He who has a {0} to live can bear almost any {1}.",
        "The unexamined {0} is not worth {1}.",
        "We are what we repeatedly {0}. Excellence then is a {1}.",
        "Simplicity is the {0} of sophistication.",
        "Not all those who {0} are lost.",
        "Knowing yourself is the beginning of all {0}.",
    ];

    private static readonly string[] PerfWords =
    [
        "wisdom", "courage", "freedom", "knowledge", "truth",
        "justice", "power", "love", "peace", "virtue",
        "reason", "strength", "hope", "light",
    ];

    public static async Task SeedPerfDataAsync(AppDbContext db)
    {
        const int targetQuoteCount      = 500;
        const int targetCollectionCount = 5;
        const int itemsPerCollection    = 20;

        var quoteCount      = await db.Quotes.CountAsync();
        var collectionCount = await db.Collections.CountAsync();

        if (quoteCount >= targetQuoteCount && collectionCount >= targetCollectionCount)
            return;

        if (quoteCount < targetQuoteCount)
        {
            var rng    = new Random(42);
            var needed = targetQuoteCount - quoteCount;

            var batch = Enumerable.Range(0, needed).Select(i =>
            {
                var tpl  = PerfTemplates[i % PerfTemplates.Length];
                var text = string.Format(tpl, PerfWords[rng.Next(PerfWords.Length)], PerfWords[rng.Next(PerfWords.Length)]);
                return Quote.Create(PerfAuthors[i % PerfAuthors.Length], text);
            }).ToList();

            db.Quotes.AddRange(batch);
            await db.SaveChangesAsync();
        }

        if (collectionCount < targetCollectionCount)
        {
            var savedQuoteIds = await db.Quotes
                .OrderBy(q => q.Id)
                .Select(q => q.Id)
                .Take(targetCollectionCount * itemsPerCollection)
                .ToListAsync();

            for (int c = collectionCount; c < targetCollectionCount; c++)
            {
                var col = new Collection($"PerfCollection-{c + 1}", "perf-seed-owner");
                foreach (var qid in savedQuoteIds.Skip(c * itemsPerCollection).Take(itemsPerCollection))
                    col.AddItem(qid);
                db.Collections.Add(col);
            }

            await db.SaveChangesAsync();
        }
    }
}
