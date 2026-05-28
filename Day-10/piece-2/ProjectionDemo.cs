using Microsoft.EntityFrameworkCore;
using QueryTranslationDemo.Dtos;

namespace QueryTranslationDemo;

public static class ProjectionDemo
{
    // ── Demo 2: Full entity vs .Select() projection — SQL column diff ─────
    // Loading a full entity tells EF to SELECT every mapped column.
    // A .Select(p => new Dto { ... }) projection pushes the column list into
    // the SQL: EF emits only the columns that the constructor references.
    //
    // WHY this matters:
    //   SQL Server must read the full row from the data page regardless,
    //   but it returns a narrower result set over the network.
    //   For wide tables (20+ columns, large NVARCHAR blobs) the bandwidth
    //   and allocation difference is measurable.
    //   A covering index on only the projected columns also becomes possible,
    //   letting SQL Server satisfy the query from the index alone (no key lookup).
    //
    // RULE: project to a DTO on every read-only path that does not need
    //       all columns.  The logged SQL tells you exactly what you saved.
    public static async Task RunAsync(AppDbContext db)
    {
        Console.WriteLine("\n── Demo 2: Full entity vs projection — SQL column diff ──────────");

        // ── BEFORE: full entity — EF selects all five columns ────────────
        Console.WriteLine("  [BEFORE — full entity: db.Products.AsNoTracking().Take(5).ToListAsync()]");
        Console.WriteLine("  Expected SQL columns: ProductId, Name, Category, Price, Stock");
        Console.WriteLine("  ─────────────────── EF log ────────────────────────────────────");

        var fullEntities = await db.Products
            .AsNoTracking()
            .Take(5)
            .ToListAsync();

        Console.WriteLine("  ─────────────────── end log ───────────────────────────────────");
        Console.WriteLine($"  Returned {fullEntities.Count} Product objects — 5 columns each\n");
        Console.WriteLine(new string('─', 66));

        // ── AFTER: projection — EF selects only the three needed columns ──
        Console.WriteLine("\n  [AFTER — projection to ProductSummaryDto]");
        Console.WriteLine("  Expected SQL columns: ProductId, Name, Category  (Price + Stock absent)");
        Console.WriteLine("  ─────────────────── EF log ────────────────────────────────────");

        var dtos = await db.Products
            .AsNoTracking()
            .Take(5)
            .Select(p => new ProductSummaryDto
            {
                ProductId = p.ProductId,
                Name      = p.Name,
                Category  = p.Category,
            })
            .ToListAsync();

        Console.WriteLine("  ─────────────────── end log ───────────────────────────────────");
        Console.WriteLine($"  Returned {dtos.Count} ProductSummaryDto objects — 3 columns each");
        Console.WriteLine("  ↑ Price and Stock are absent from the SQL above.");
        Console.WriteLine("    They were never fetched, never allocated, never sent over the wire.");
    }

    // ── Demo 3: WHERE + projection — filter and column list in one SQL ────
    // Combining .Where() before .Select() shows that EF pushes both the
    // predicate (WHERE clause) and the column list (SELECT list) into a
    // single SQL statement.  No extra round-trip, no extra columns.
    //
    // This is the pattern for any read-only list endpoint:
    //   .Where(predicate)          → SQL WHERE
    //   .OrderBy(key)              → SQL ORDER BY
    //   .Select(p => new Dto {...}) → narrow SELECT list
    //   .Take(n)                   → SQL TOP(n)
    //   .ToListAsync()             → materialise only what was asked for
    public static async Task RunFilteredProjectionAsync(AppDbContext db)
    {
        Console.WriteLine("\n── Demo 3: WHERE + projection — one SQL, narrow result ──────────");
        Console.WriteLine("  [query: Where(Category==\"Electronics\") + OrderBy(Price) + Select to DTO + Take(5)]");
        Console.WriteLine("  ─────────────────── EF log ────────────────────────────────────");

        var electronics = await db.Products
            .AsNoTracking()
            .Where(p => p.Category == "Electronics")
            .OrderBy(p => p.Price)
            .Select(p => new ProductSummaryDto
            {
                ProductId = p.ProductId,
                Name      = p.Name,
                Category  = p.Category,
            })
            .Take(5)
            .ToListAsync();

        Console.WriteLine("  ─────────────────── end log ───────────────────────────────────\n");
        Console.WriteLine($"  Rows : {electronics.Count}");
        foreach (var d in electronics)
            Console.WriteLine($"    [{d.ProductId,6}] {d.Name,-20} {d.Category}");
        Console.WriteLine();
        Console.WriteLine("  ↑ WHERE, ORDER BY, TOP, and the narrow SELECT were all inside");
        Console.WriteLine("    one SQL statement.  Nothing evaluated in C# until materialisation.");
    }
}
