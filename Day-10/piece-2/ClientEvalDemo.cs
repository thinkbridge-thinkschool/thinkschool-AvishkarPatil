using Microsoft.EntityFrameworkCore;

namespace QueryTranslationDemo;

public static class ClientEvalDemo
{
    // ── Demo 4: Accidental client-side evaluation — caught and fixed ──────
    // EF Core 3+ throws InvalidOperationException for expressions it cannot
    // translate to SQL.  But one trap does NOT throw — it silently fetches
    // far more data than needed:
    //
    //   Inserting .AsEnumerable() mid-query shifts the evaluation boundary
    //   from the database to the client.  Everything AFTER that call becomes
    //   LINQ to Objects — executed in C# after EF has already fetched rows.
    //
    // THE TRAP:
    //   db.Products                         ← IQueryable<Product> — no SQL yet
    //     .AsNoTracking()
    //     .AsEnumerable()                   ← switches to IEnumerable<Product>
    //     .Where(p => p.Price < 5m)         ← runs in C#, NOT translated to SQL WHERE
    //     .Take(10)                         ← runs in C#, NOT translated to SQL TOP
    //     .ToList()                         ← iterates IEnumerable → SQL fires here
    //
    //   SQL sent: SELECT all columns FROM [Products]  (all 10 000 rows!)
    //   C# then: scans 10 000 objects, keeps ~50, returns first 10
    //
    // HOW TO CATCH IT:
    //   Read the logged SQL.  A bare SELECT with no WHERE and no TOP when you
    //   expected a filtered query is the signature of accidental client eval.
    //
    // THE FIX:
    //   Remove .AsEnumerable(). Keep the full pipeline as IQueryable<T> until
    //   .ToListAsync().  Where() and Take() are then translated to SQL.
    //
    //   SQL sent: SELECT TOP(10) ... FROM [Products] WHERE [Price] < 5.0
    //   (10 rows, not 10 000)
    public static async Task RunAsync(AppDbContext db)
    {
        Console.WriteLine("\n── Demo 4: Client-side evaluation — caught and fixed ────────────");

        // ── BEFORE (broken) — .AsEnumerable() forces a full table scan ───
        Console.WriteLine("  [BEFORE — .AsEnumerable() inserted mid-query]");
        Console.WriteLine("  Intent  : fetch 10 products with Price < 5");
        Console.WriteLine("  Reality : SQL fetches ALL 10 000 rows; C# does the filtering");
        Console.WriteLine("  ─────────────────── EF log ────────────────────────────────────");

        // .AsEnumerable() here is the bug. The SQL that fires has no WHERE and no TOP.
        // All 10 000 rows cross the network before C# applies the filter.
        var broken = db.Products
            .AsNoTracking()
            .AsEnumerable()               // ← evaluation boundary shifts here
            .Where(p => p.Price < 5m)     // ← C# filter, not SQL WHERE
            .Take(10)                      // ← C# take, not SQL TOP
            .ToList();                     // ← all 10 000 rows already fetched by this point

        Console.WriteLine("  ─────────────────── end log ───────────────────────────────────");
        Console.WriteLine($"  Rows returned : {broken.Count}");
        Console.WriteLine("  ↑ Check the SQL above — no WHERE clause, no TOP.");
        Console.WriteLine("    All 10 000 rows crossed the network. 10 were needed.\n");
        Console.WriteLine(new string('─', 66));

        // ── AFTER (fixed) — filter and take stay in IQueryable ───────────
        Console.WriteLine("\n  [AFTER — .Where() and .Take() before materialisation, no .AsEnumerable()]");
        Console.WriteLine("  Expected SQL: WHERE [Price] < 5.0  +  TOP(10)");
        Console.WriteLine("  ─────────────────── EF log ────────────────────────────────────");

        var fixed_ = await db.Products
            .AsNoTracking()
            .Where(p => p.Price < 5m)     // ← translated to SQL WHERE [p].[Price] < 5.0
            .Take(10)                      // ← translated to SQL TOP(10)
            .ToListAsync();               // ← only the matching rows are fetched

        Console.WriteLine("  ─────────────────── end log ───────────────────────────────────");
        Console.WriteLine($"  Rows returned : {fixed_.Count}");
        Console.WriteLine("  ↑ Compare the SQL above — WHERE and TOP are present.");
        Console.WriteLine("    Only the matching rows crossed the network.");
        Console.WriteLine();
        Console.WriteLine("  Root cause  : .AsEnumerable() silently shifts evaluation from");
        Console.WriteLine("                SQL Server to the C# heap. No exception is thrown.");
        Console.WriteLine("  Detection   : log the SQL and look for a bare SELECT with no WHERE");
        Console.WriteLine("                when your code has a .Where() or .Take().");
        Console.WriteLine("  Fix         : keep the full pipeline as IQueryable<T>; only call");
        Console.WriteLine("                .ToList() / .ToListAsync() at the very end.");
    }
}
