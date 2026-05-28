using Microsoft.EntityFrameworkCore;

namespace ChangeTrackerDemo;

public static class TrackingDemo
{
    // ── Demo 1: Identity resolution ──────────────────────────────────────
    // EF Core maintains an identity map keyed by primary key.
    // When a tracked query returns a row, EF checks the identity map after
    // materialising the entity.  If the PK already exists there, EF discards
    // the new object and returns the existing tracked instance instead.
    //
    // IMPORTANT DISTINCTION:
    //   FirstAsync()  — the SQL query fires EVERY time. Identity resolution
    //                   happens at materialisation (after the DB responds), not
    //                   before the query.  Two DB round-trips, one object.
    //   FindAsync(id) — checks the identity map BEFORE sending any SQL.
    //                   If a match is found, no SELECT is executed at all.
    //                   This is the genuine "no round-trip" path.
    public static async Task RunIdentityResolutionAsync(AppDbContext db)
    {
        Console.WriteLine("\n── Demo 1: Identity resolution (tracking ON) ───────────────────");

        // PART A: FirstAsync — two SQL queries, one tracked object
        // SELECT TOP(1) fires twice. On the second return, EF finds ProductId=1
        // already in the identity map and returns the existing instance.
        var first  = await db.Products.FirstAsync();
        var second = await db.Products.FirstAsync();

        Console.WriteLine("  [FirstAsync x2 — DB queried twice, identity map returns same object]");
        Console.WriteLine($"  first.ProductId   : {first.ProductId}");
        Console.WriteLine($"  second.ProductId  : {second.ProductId}");
        Console.WriteLine($"  ReferenceEquals   : {ReferenceEquals(first, second)}");
        // ↑ True — same tracked instance. Two DB round-trips happened.
        Console.WriteLine($"  EntityState       : {db.Entry(first).State}");
        Console.WriteLine($"  Tracked entities  : {db.ChangeTracker.Entries().Count()}");

        // PART B: FindAsync — identity map consulted BEFORE any SQL
        // Because ProductId=1 is already tracked (from PART A), FindAsync
        // returns the cached object immediately. No SELECT is sent.
        // Proof: open SQL Server Profiler before running — only 2 queries
        // fire total (both from PART A), not 3.
        var found = await db.Products.FindAsync(first.ProductId);

        Console.WriteLine("\n  [FindAsync — identity map checked first, DB skipped if found]");
        Console.WriteLine($"  found.ProductId               : {found!.ProductId}");
        Console.WriteLine($"  ReferenceEquals(first, found) : {ReferenceEquals(first, found)}");
        // ↑ True — and this time genuinely no SELECT was sent to SQL Server

        db.ChangeTracker.Clear();
    }

    // ── Demo 2: Mutation detection ───────────────────────────────────────
    // The snapshot stored by the change tracker makes mutation detection
    // automatic — you change a property and EF transitions the entry's
    // state from Unchanged to Modified with no extra code.
    //
    // WHY this costs memory:
    //   For each tracked entity EF allocates an EntityEntry that holds
    //   original values for every property.  With 10 000 entities that is
    //   10 000 * (number of columns) extra objects on the heap.
    //   For read-only paths you pay this cost for nothing.
    public static async Task RunMutationDetectionAsync(AppDbContext db)
    {
        Console.WriteLine("\n── Demo 2: Mutation detection (WHY tracking costs) ─────────────");

        var product = await db.Products.FirstAsync();
        Console.WriteLine($"  State before change : {db.Entry(product).State}");
        // ↑ Unchanged

        // Modify a property in memory — no DB call.
        product.Price = 0.01m;
        Console.WriteLine($"  State after change  : {db.Entry(product).State}");
        // ↑ Modified — change tracker detected the diff automatically

        // Peek at the original value still held in the snapshot.
        var originalPrice = db.Entry(product).OriginalValues[nameof(product.Price)];
        Console.WriteLine($"  Original price (snapshot) : {originalPrice}");
        Console.WriteLine($"  Current  price (in-memory): {product.Price}");

        // Discard — do not call SaveChanges.
        db.ChangeTracker.Clear();
    }
}
