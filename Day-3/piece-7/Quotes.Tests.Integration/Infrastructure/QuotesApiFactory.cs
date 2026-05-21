using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using QuotesApi.Data;
using QuotesApi.Services;

namespace Quotes.Tests.Integration.Infrastructure;

/// <summary>
/// Boots the real app pipeline in-process, replacing the SQLite file DB with an
/// isolated SQL Server database inside the shared Testcontainers instance.
///
/// Each test gets its own QuotesApiFactory → its own GUID-named database → zero
/// shared state between tests.  Schema is created with EnsureCreated (faster than
/// running migrations in a test context) and seeded by DbSeeder.
/// </summary>
public sealed class QuotesApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public FakeClock Clock { get; } = new();

    public QuotesApiFactory(string serverConnectionString)
    {
        // Stamp a unique catalog name so parallel tests never share a database.
        var builder = new SqlConnectionStringBuilder(serverConnectionString)
        {
            InitialCatalog = $"QuotesTest_{Guid.NewGuid():N}"
        };
        _connectionString = builder.ConnectionString;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Tell Program.cs to skip its Migrate()+Seed() block.
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Swap SQLite for SQL Server.
            var dbDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (dbDescriptor is not null)
                services.Remove(dbDescriptor);

            services.AddDbContext<AppDbContext>(options =>
                options.UseSqlServer(_connectionString));

            // Replace IClock so tests can freeze or advance time.
            var clockDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(IClock));
            if (clockDescriptor is not null)
                services.Remove(clockDescriptor);

            services.AddSingleton<IClock>(Clock);
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        // Program.cs skipped migrations — we create the schema from the current
        // model and seed the test users + sample quote.
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureDeleted();   // no-op for a brand-new GUID database
        db.Database.EnsureCreated();   // creates schema without migration history
        DbSeeder.SeedAsync(db).GetAwaiter().GetResult();

        return host;
    }
}
