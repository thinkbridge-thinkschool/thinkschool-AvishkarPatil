using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QueryTranslationDemo;

Console.WriteLine("═══════════════════════════════════════════════════════════════");
Console.WriteLine("  Day 10 · Piece 2 — Query Translation + Projections");
Console.WriteLine("═══════════════════════════════════════════════════════════════");
Console.WriteLine("  Database : EfTrackingDemo  (.\\SQLEXPRESS)");
Console.WriteLine("  Table    : dbo.Products    (10 000 rows, seeded by piece-1)");
Console.WriteLine("  Logging  : DbLoggerCategory.Database.Command + SensitiveDataLogging");

// ① SQL logging demo — creates its own context to show the configuration explicitly.
Console.WriteLine("\n① SQL logging");
await SqlLoggingDemo.RunAsync();

// ② Projection demos — share one logged context; each demo clears the tracker between runs.
// LogTo is configured here: filtered to SQL command events only so the output stays readable.
Console.WriteLine("\n② Projection demos");
var loggedOptions = new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlServer(AppDbContext.ConnectionString)
    .LogTo(
        msg  => Console.WriteLine(msg),
        new[] { DbLoggerCategory.Database.Command.Name },
        LogLevel.Information)
    .EnableSensitiveDataLogging()
    .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
    .Options;

await using (var db = new AppDbContext(loggedOptions))
{
    await ProjectionDemo.RunAsync(db);
    await ProjectionDemo.RunFilteredProjectionAsync(db);
}

// ③ Client-side evaluation — caught and fixed.
Console.WriteLine("\n③ Client-side evaluation");
await using (var db = new AppDbContext(loggedOptions))
{
    await ClientEvalDemo.RunAsync(db);
}

Console.WriteLine("\n═══════════════════════════════════════════════════════════════");
Console.WriteLine("  Done.");
Console.WriteLine("═══════════════════════════════════════════════════════════════");
