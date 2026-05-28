using Microsoft.EntityFrameworkCore;
using QueryTranslationDemo.Models;

namespace QueryTranslationDemo;

public class AppDbContext : DbContext
{
    // Reuses the EfTrackingDemo database created and seeded by Day-10 piece-1.
    // 10 000 Product rows are already present — no EnsureCreated or seeding needed.
    public const string ConnectionString =
        @"Server=.\SQLEXPRESS;Database=EfTrackingDemo;Trusted_Connection=true;TrustServerCertificate=true;";

    // Parameterless constructor: used by Program.cs sections that build their own options.
    public AppDbContext() { }

    // Options constructor: used when the caller injects LogTo or other overrides.
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Product> Products => Set<Product>();

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        if (!options.IsConfigured)
            options.UseSqlServer(ConnectionString);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(e =>
        {
            e.HasKey(p => p.ProductId);
            e.Property(p => p.Name).HasMaxLength(100).IsRequired();
            e.Property(p => p.Category).HasMaxLength(50).IsRequired();
            e.Property(p => p.Price).HasColumnType("decimal(10,2)");
        });
    }
}
