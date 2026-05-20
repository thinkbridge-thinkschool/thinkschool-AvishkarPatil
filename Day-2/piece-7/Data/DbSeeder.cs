using QuotesApi.Models;

namespace QuotesApi.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        if (db.Users.Any())
            return;

        // dev seed only — override credentials via env/secrets before exposing publicly
        var user = User.Create("demo@example.com", "P@ssw0rd!");
        db.Users.Add(user);
        await db.SaveChangesAsync();
    }
}
