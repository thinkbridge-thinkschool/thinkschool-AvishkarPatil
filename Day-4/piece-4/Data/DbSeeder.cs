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
}
