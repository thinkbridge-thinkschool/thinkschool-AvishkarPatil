using ChangeTrackerDemo.Models;
using Microsoft.EntityFrameworkCore;

namespace ChangeTrackerDemo;

public class AppDbContext : DbContext
{
    // SQL Server Express, Windows Authentication, Trust Server Certificate.
    // Matches the .\SQLEXPRESS instance visible in SSMS connection history.
    public const string ConnectionString =
        @"Server=.\SQLEXPRESS;Database=EfTrackingDemo;Trusted_Connection=true;TrustServerCertificate=true;";

    // Parameterless constructor: used by all demo sections (OnConfiguring applies ConnectionString).
    public AppDbContext() { }

    // Options constructor: used when caller wants to override options (e.g. UseQueryTrackingBehavior).
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Product> Products => Set<Product>();

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        // Only apply the default connection string if the caller did not already configure options.
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
