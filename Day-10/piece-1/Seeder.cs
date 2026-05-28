using ChangeTrackerDemo.Models;
using Microsoft.EntityFrameworkCore;

namespace ChangeTrackerDemo;

public static class Seeder
{
    private static readonly string[] Categories =
        ["Electronics", "Clothing", "Books", "Food", "Tools", "Sports", "Toys", "Garden"];

    public static async Task SeedAsync(AppDbContext db)
    {
        int existing = await db.Products.CountAsync();
        if (existing >= 10_000)
        {
            Console.WriteLine($"  {existing:N0} rows already present — skipping seed.");
            return;
        }

        Console.WriteLine("  Seeding 10 000 products in batches of 1 000...");

        // Insert in batches of 1 000.
        // After each SaveChanges we call ChangeTracker.Clear() to release the
        // 1 000 tracked entities before the next batch — otherwise the change
        // tracker accumulates all 10 000 entries in memory during seeding.
        var products = Enumerable.Range(1, 10_000).Select(i => new Product
        {
            Name     = $"Product-{i:D5}",
            Category = Categories[i % Categories.Length],
            Price    = Math.Round(0.99m + (i % 1000), 2),
            Stock    = i % 500
        });

        foreach (var batch in products.Chunk(1_000))
        {
            db.Products.AddRange(batch);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }

        Console.WriteLine("  Seed complete.");
    }
}
