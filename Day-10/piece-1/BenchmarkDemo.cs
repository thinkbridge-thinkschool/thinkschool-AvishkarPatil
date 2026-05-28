using System.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace ChangeTrackerDemo;

public static class BenchmarkDemo
{
    // ── Demo 8: Tracked vs AsNoTracking — timing + allocation ────────────
    // Both paths issue the identical SQL query ("SELECT * FROM Products").
    // The difference is entirely in what EF does AFTER fetching each row:
    //
    //   Tracked path:
    //     1. Materialise the entity object
    //     2. Allocate an EntityEntry to wrap it
    //     3. Snapshot all property values into an ISnapshot (original values)
    //     4. Insert into the identity map (keyed by PK)
    //     Total: ~4–5 extra allocations per entity, plus the snapshot array
    //
    //   AsNoTracking path:
    //     1. Materialise the entity object
    //     Done. No entry, no snapshot, no map insertion.
    //
    // WHY synchronous .ToList() is used here instead of .ToListAsync():
    //   GC.GetAllocatedBytesForCurrentThread() is per-thread. In async code,
    //   the continuation after 'await' can resume on a different thread pool
    //   thread. That makes the before/after delta meaningless — you measure
    //   thread A's baseline against thread B's total. Synchronous calls keep
    //   all work on the calling thread, making the measurement reliable.
    public static Task RunAsync(AppDbContext db)
    {
        Console.WriteLine("\n── Demo 8: Benchmark — tracked vs AsNoTracking (10 000 rows) ───");
        Console.WriteLine("  5 iterations each. Synchronous queries for reliable allocation measurement.");
        Console.WriteLine("  Warmup pass discarded. Times include SQL round-trip.\n");

        // ── Warmup ───────────────────────────────────────────────────────
        // One unmeassured iteration of each path before the clock starts.
        // This forces JIT compilation of all EF Core materialiser code and
        // warms the SQL Server query-plan cache so neither path pays a
        // first-run penalty during measurement.
        _ = db.Products.ToList();
        db.ChangeTracker.Clear();
        _ = db.Products.AsNoTracking().ToList();
        Console.WriteLine("  Warmup done. Starting measurement...\n");

        const int iterations = 5;

        long trackedMs    = 0, trackedBytes    = 0;
        long noTrackingMs = 0, noTrackingBytes = 0;

        // ── Tracked iterations ───────────────────────────────────────────
        for (int i = 0; i < iterations; i++)
        {
            db.ChangeTracker.Clear();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long before = GC.GetAllocatedBytesForCurrentThread();
            var  sw     = Stopwatch.StartNew();

            // Synchronous — all allocations happen on this thread.
            var list = db.Products.ToList();

            sw.Stop();
            trackedMs    += sw.ElapsedMilliseconds;
            trackedBytes += GC.GetAllocatedBytesForCurrentThread() - before;

            db.ChangeTracker.Clear();
            _ = list.Count;                    // prevent dead-code elimination
        }

        // ── AsNoTracking iterations ──────────────────────────────────────
        for (int i = 0; i < iterations; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long before = GC.GetAllocatedBytesForCurrentThread();
            var  sw     = Stopwatch.StartNew();

            // Same synchronous pattern — identical SQL, no tracking overhead.
            var list = db.Products.AsNoTracking().ToList();

            sw.Stop();
            noTrackingMs    += sw.ElapsedMilliseconds;
            noTrackingBytes += GC.GetAllocatedBytesForCurrentThread() - before;

            _ = list.Count;
        }

        // ── Results ──────────────────────────────────────────────────────
        double avgTrackedMs    = (double)trackedMs    / iterations;
        double avgNoTrackingMs = (double)noTrackingMs / iterations;
        double avgTrackedKB    = trackedBytes    / iterations / 1024.0;
        double avgNoTrackingKB = noTrackingBytes / iterations / 1024.0;

        Console.WriteLine($"  {"Metric",-26} {"Tracked",10} {"AsNoTracking",14} {"Saved",10}");
        Console.WriteLine($"  {new string('-', 64)}");
        Console.WriteLine($"  {"Avg time (ms)",-26} {avgTrackedMs,10:F1} {avgNoTrackingMs,14:F1} {avgTrackedMs - avgNoTrackingMs,+10:F1}");
        Console.WriteLine($"  {"Avg allocated (KB)",-26} {avgTrackedKB,10:F0} {avgNoTrackingKB,14:F0} {avgTrackedKB - avgNoTrackingKB,+10:F0}");

        Console.WriteLine();
        Console.WriteLine("  Interpretation:");
        Console.WriteLine("    The allocation gap IS the change-tracker tax — EntityEntry objects,");
        Console.WriteLine("    property snapshots, and identity-map inserts that AsNoTracking skips.");
        Console.WriteLine("    The time gap tends to widen as entity count or column count grows.");
        Console.WriteLine();
        Console.WriteLine("  Practical rule:");
        Console.WriteLine("    Default to AsNoTracking() on every read-only query.");
        Console.WriteLine("    Call ToList() / FirstAsync() without it only when you intend to");
        Console.WriteLine("    modify the entity and call SaveChanges() in the same DbContext scope.");

        return Task.CompletedTask;
    }
}
