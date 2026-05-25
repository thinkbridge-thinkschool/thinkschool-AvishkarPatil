using Testcontainers.MsSql;

namespace Quotes.Tests.Integration.Infrastructure;

/// <summary>
/// Starts one SQL Server 2022 container for the entire test collection and tears
/// it down after all tests finish.  Shared via xUnit ICollectionFixture so the
/// costly container-start only happens once per test run.
/// </summary>
public sealed class SqlServerContainerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    /// <summary>Master connection string pointing at the container's default database.</summary>
    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

/// <summary>
/// xUnit collection definition — attaching this to a test class causes xUnit to
/// inject <see cref="SqlServerContainerFixture"/> once for all classes in the
/// collection, keeping the container alive across every test.
/// </summary>
[CollectionDefinition("SqlServer")]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerContainerFixture> { }
