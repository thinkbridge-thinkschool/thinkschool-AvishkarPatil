using System.Net.Http.Headers;
using System.Net.Http.Json;
using QuotesApi.DTOs;

namespace Quotes.Tests.Integration.Infrastructure;

/// <summary>
/// Base class for SQL Server integration tests.  xUnit creates a new instance per
/// test method, so each test gets its own QuotesApiFactory (→ its own isolated
/// database) and its own HttpClient — zero shared state between tests.
///
/// The <see cref="SqlServerContainerFixture"/> is injected by xUnit from the
/// [Collection("SqlServer")] decoration on each concrete test class; the single
/// container is reused across all tests to avoid repeated docker-pull costs.
/// </summary>
public abstract class IntegrationTestBase : IDisposable
{
    protected readonly QuotesApiFactory Factory;
    protected readonly HttpClient Client;

    protected IntegrationTestBase(SqlServerContainerFixture fixture)
    {
        Factory = new QuotesApiFactory(fixture.ConnectionString);
        Client = Factory.CreateClient();
    }

    // -----------------------------------------------------------------------
    // Auth helpers
    // -----------------------------------------------------------------------

    protected async Task<LoginResponse> LoginAsync(string email, string password = "P@ssw0rd!")
    {
        var response = await Client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(email, password));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    /// <summary>Sets the Bearer token on <see cref="Client"/> to a fresh writer token.</summary>
    protected async Task UseWriterTokenAsync()
    {
        var tokens = await LoginAsync("demo@example.com");
        Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
    }

    /// <summary>Sets the Bearer token on <see cref="Client"/> to a fresh viewer token.</summary>
    protected async Task UseViewerTokenAsync()
    {
        var tokens = await LoginAsync("reader@example.com");
        Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
    }

    // -----------------------------------------------------------------------
    // IDisposable
    // -----------------------------------------------------------------------

    public void Dispose()
    {
        Client.Dispose();
        Factory.Dispose();
    }
}
