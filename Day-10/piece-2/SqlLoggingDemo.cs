using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace QueryTranslationDemo;

public static class SqlLoggingDemo
{
    // ── Demo 1: SQL logging — see what EF sends to the database ──────────
    // By default EF Core does not print the SQL it generates.
    // Two options in DbContextOptionsBuilder expose it:
    //
    //   LogTo(Action<string>, IEnumerable<string>, LogLevel)
    //     — pipes every matching log message to the supplied delegate.
    //       Filtering to DbLoggerCategory.Database.Command keeps only the
    //       SQL execution lines and drops provider/model/migration noise.
    //
    //   EnableSensitiveDataLogging()
    //     — includes actual parameter values in the log output.
    //       OFF by default because values may contain PII or secrets.
    //       Enable in local development; NEVER enable in production.
    //
    // This demo creates the DbContext with both options wired up, runs a
    // single bounded query, and lets you read the exact SQL EF generated.
    // The SQL block appears inline — look for "Executed DbCommand" in the output.
    public static async Task RunAsync()
    {
        Console.WriteLine("\n── Demo 1: SQL logging setup ────────────────────────────────────");
        Console.WriteLine("  LogTo(DbLoggerCategory.Database.Command) + EnableSensitiveDataLogging");
        Console.WriteLine("  ↓ EF-generated SQL appears immediately below each query\n");

        // Wire up logging explicitly so the configuration is visible here in the demo.
        // In a real application this would live in AddDbContext<T>() at startup.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(AppDbContext.ConnectionString)
            .LogTo(
                msg  => Console.WriteLine(msg),
                new[] { DbLoggerCategory.Database.Command.Name },
                LogLevel.Information)
            .EnableSensitiveDataLogging()
            .Options;

        await using var db = new AppDbContext(options);

        // ── Single query — full entity, all five columns ──────────────────
        // Watch the logged SQL block for the SELECT list.
        // EF selects ProductId, Name, Category, Price, Stock even though the
        // display below only uses three of them.  That is the waste we fix in Demo 2.
        Console.WriteLine("  [query: db.Products.AsNoTracking().Take(3).ToListAsync()]");
        Console.WriteLine("  ─────────────────── EF log ────────────────────────────────────");

        var sample = await db.Products
            .AsNoTracking()
            .Take(3)
            .ToListAsync();

        Console.WriteLine("  ─────────────────── end log ───────────────────────────────────\n");
        Console.WriteLine($"  Rows returned  : {sample.Count}");
        Console.WriteLine($"  First product  : {sample[0].Name,-20} | {sample[0].Category,-12} | {sample[0].Price,8:C}");
        Console.WriteLine("  ↑ All five columns were selected even though only three appear above.");
        Console.WriteLine("    Price and Stock crossed the network for nothing.");
    }
}
