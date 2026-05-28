using ChangeTrackerDemo;
using Microsoft.EntityFrameworkCore;

Console.WriteLine("═══════════════════════════════════════════════════════════════");
Console.WriteLine("  Day 10 · Piece 1 — EF Core Change Tracker + AsNoTracking");
Console.WriteLine("═══════════════════════════════════════════════════════════════");

// ① Setup — create the database and schema if they do not already exist.
//    EnsureCreated() is used here because this is a demo app, not a
//    production service.  In production use migrations (dotnet ef migrations).
Console.WriteLine("\n① Setup");
await using (var db = new AppDbContext())
{
    bool created = await db.Database.EnsureCreatedAsync();
    Console.WriteLine(created ? "  Database created." : "  Database already exists.");
}

// ② Seed — insert 10 000 Product rows if the table is empty.
//    Seeding uses a fresh DbContext so the seeded entities are not left
//    in the change tracker before the demos start.
Console.WriteLine("\n② Seed");
await using (var db = new AppDbContext())
{
    await Seeder.SeedAsync(db);
}

// ③ Tracking demos — run with a dedicated DbContext that is disposed
//    after each section so there is no leftover tracking state.
Console.WriteLine("\n③ Tracking demos");
await using (var db = new AppDbContext())
{
    await TrackingDemo.RunIdentityResolutionAsync(db);
    await TrackingDemo.RunMutationDetectionAsync(db);
}

// ④ AsNoTracking demos — includes the context-level UseQueryTrackingBehavior
//    variant which creates its own DbContext internally.
Console.WriteLine("\n④ AsNoTracking demos");
await using (var db = new AppDbContext())
{
    await NoTrackingDemo.RunNoIdentityResolutionAsync(db);
}
await using (var db = new AppDbContext())
{
    await NoTrackingDemo.RunSilentFailureDemoAsync(db);
}
await NoTrackingDemo.RunContextLevelNoTrackingAsync();

// ⑤ Benchmark — tracked vs AsNoTracking on the full 10 000-row table.
Console.WriteLine("\n⑤ Benchmark");
await using (var db = new AppDbContext())
{
    await BenchmarkDemo.RunAsync(db);
}

Console.WriteLine("═══════════════════════════════════════════════════════════════");
Console.WriteLine("  Done.");
Console.WriteLine("═══════════════════════════════════════════════════════════════");
