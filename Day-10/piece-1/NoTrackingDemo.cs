using Microsoft.EntityFrameworkCore;

namespace ChangeTrackerDemo;

public static class NoTrackingDemo
{
    // ── Demo 3: AsNoTracking — no identity resolution ────────────────────
    // AsNoTracking() instructs EF to materialise entities as plain CLR
    // objects and skip all change-tracker registration.
    //
    // Consequences:
    //   ✓ No snapshot allocated per entity
    //   ✓ No identity map lookup/insert per entity
    //   ✓ Lower memory pressure, faster materialisation
    //   ✗ ReferenceEquals returns false for two queries of the same row
    //   ✗ Calling SaveChanges on an untracked entity does nothing
    //   ✗ Navigation property cross-wiring may not happen correctly
    //
    // Rule of thumb: use AsNoTracking() on every query that feeds a
    // read-only path (GET endpoints, reports, projections, exports).
    public static async Task RunNoIdentityResolutionAsync(AppDbContext db)
    {
        Console.WriteLine("\n── Demo 3: No identity resolution (AsNoTracking) ───────────────");

        var first  = await db.Products.AsNoTracking().FirstAsync();
        var second = await db.Products.AsNoTracking().FirstAsync();

        Console.WriteLine($"  first.ProductId   : {first.ProductId}");
        Console.WriteLine($"  second.ProductId  : {second.ProductId}");
        Console.WriteLine($"  ReferenceEquals   : {ReferenceEquals(first, second)}");
        // ↑ false — two independent heap allocations, no shared identity map

        // The entity is detached — the change tracker has no record of it.
        var entry = db.Entry(first);
        Console.WriteLine($"  EntityState       : {entry.State}");
        // ↑ Detached — EF cannot track changes to this object

        Console.WriteLine($"  Tracked entities  : {db.ChangeTracker.Entries().Count()}");
        // ↑ 0 — nothing was registered
    }

    // ── Demo 5: AsNoTracking + SaveChanges = silent failure ──────────────
    // The most dangerous AsNoTracking pitfall: you load an entity, modify it,
    // call SaveChanges — and zero rows are affected. No exception, no warning.
    // EF has no EntityEntry for this object, so the change tracker emits no
    // UPDATE statement. The in-memory value diverges silently from the DB.
    //
    // How to catch it: check the int returned by SaveChanges[Async].
    // 0 rows affected when you expected 1 is the only signal EF gives you.
    public static async Task RunSilentFailureDemoAsync(AppDbContext db)
    {
        Console.WriteLine("\n── Demo 5: AsNoTracking + SaveChanges = silent failure ──────────");

        // Load without tracking — EF materialises but does NOT register the entity.
        var product       = await db.Products.AsNoTracking().FirstAsync();
        var originalPrice = product.Price;

        // Modify in memory. No EntityEntry exists, so no state transition occurs.
        product.Price = 0.01m;

        // SaveChanges emits no UPDATE — the change tracker has nothing to flush.
        int rows = await db.SaveChangesAsync();

        // Re-read from DB to confirm the price was never written.
        var fromDb = await db.Products
            .AsNoTracking()
            .FirstAsync(p => p.ProductId == product.ProductId);

        Console.WriteLine($"  Modified price (in-memory)    : {product.Price}");
        Console.WriteLine($"  SaveChanges rows affected      : {rows}");
        // ↑ 0 — no UPDATE was sent
        Console.WriteLine($"  Price in DB after SaveChanges  : {fromDb.Price}");
        // ↑ original value — DB was untouched
        Console.WriteLine($"  EntityState                    : {db.Entry(product).State}");
        // ↑ Detached — EF still has no record of this object
        Console.WriteLine();
        Console.WriteLine("  Rule: if rows == 0 and you expected an update, the entity");
        Console.WriteLine("  was loaded with AsNoTracking and was never registered.");
        Console.WriteLine("  Fix: re-attach with db.Update(entity) or load with tracking.");
    }

    // ── Demo 7: UseQueryTrackingBehavior — context-level default ─────────
    // Instead of adding AsNoTracking() to every query, you can set the
    // default for the whole DbContext by calling UseQueryTrackingBehavior
    // in OnConfiguring.  Individual queries can still opt back in with
    // AsTracking() when you need change detection.
    //
    // This demo shows that a context configured with NoTracking behaves
    // the same as calling AsNoTracking() per query.
    public static async Task RunContextLevelNoTrackingAsync()
    {
        Console.WriteLine("\n── Demo 7: Context-level NoTracking (UseQueryTrackingBehavior) ─");

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder
            .UseSqlServer(AppDbContext.ConnectionString)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);

        await using var db = new AppDbContext(optionsBuilder.Options);

        var product = await db.Products.FirstAsync();
        Console.WriteLine($"  EntityState       : {db.Entry(product).State}");
        // ↑ Detached — context default is NoTracking so no registration happened

        Console.WriteLine($"  Tracked entities  : {db.ChangeTracker.Entries().Count()}");
        // ↑ 0
    }
}
