using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Data;
using QuotesApi.Services;

namespace Quotes.Tests.Integration.Infrastructure;

/// <summary>
/// Boots the real app pipeline in-process, replacing the SQLite file DB with an
/// in-memory connection and IClock with a controllable fake.
/// Each test that uses IntegrationTestBase gets its own instance, and therefore
/// its own SqliteConnection, so state never leaks between tests.
/// </summary>
public sealed class QuotesApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;

    public FakeClock Clock { get; } = new();

    public QuotesApiFactory(SqliteConnection connection)
    {
        _connection = connection;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Swap the file-backed DbContext for one bound to the in-memory connection.
            var dbDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (dbDescriptor is not null)
                services.Remove(dbDescriptor);

            services.AddDbContext<AppDbContext>(options =>
                options.UseSqlite(_connection));

            // Replace IClock so tests can freeze or advance time.
            var clockDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(IClock));
            if (clockDescriptor is not null)
                services.Remove(clockDescriptor);

            services.AddSingleton<IClock>(Clock);
        });
    }
}
