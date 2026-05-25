using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QuotesApi.DTOs;
using QuotesApi.Models;
using QuotesApi.Repositories;

namespace QuotesApi.Tests;

public class CancellationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public CancellationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CreateCollection_WhenCancelledMidRequest_OperationDoesNotComplete()
    {
        // Arrange
        var isCancelledInRepository = false;

        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICollectionRepository>();
                services.AddScoped<ICollectionRepository>(sp => new DelayingCollectionRepository(
                    onCancelled: () => isCancelledInRepository = true
                ));

                // Override auth so .RequireAuthorization() passes without a real token.
                services.PostConfigure<AuthenticationOptions>(o =>
                {
                    o.DefaultAuthenticateScheme = "Test";
                    o.DefaultChallengeScheme    = "Test";
                    o.DefaultForbidScheme       = "Test";
                });
                services.AddAuthentication()
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
            });
        });

        var client = factory.CreateClient();
        var cts = new CancellationTokenSource();

        var request = new CreateCollectionRequest("Test Collection");
        var content = JsonContent.Create(request);

        // Cancel after 500ms
        cts.CancelAfter(TimeSpan.FromMilliseconds(500));

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await client.PostAsync("/api/collections", content, cts.Token);
        });

        // Wait a bit to ensure the server had time to process the cancellation
        await Task.Delay(500);

        // The token must have been passed all the way to the repository
        Assert.True(isCancelledInRepository, "Cancellation did not flow to the repository layer.");
    }
}

// Always-succeed auth handler used in tests so real tokens are not required.
public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims  = new[] { new Claim(ClaimTypes.NameIdentifier, "test-user") };
        var identity = new ClaimsIdentity(claims, "Test");
        var ticket  = new AuthenticationTicket(new ClaimsPrincipal(identity), "Test");
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

public class DelayingCollectionRepository : ICollectionRepository
{
    private readonly Action _onCancelled;

    public DelayingCollectionRepository(Action onCancelled)
    {
        _onCancelled = onCancelled;
    }

    public async Task<Collection> CreateAsync(Collection collection, CancellationToken cancellationToken = default)
    {
        try
        {
            // Delay for longer than the cancellation token timeout to simulate I/O
            await Task.Delay(2000, cancellationToken);
            return collection;
        }
        catch (OperationCanceledException)
        {
            _onCancelled();
            throw;
        }
    }

    public Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<Collection?> GetByIdAsync(int id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<bool> UpdateAsync(Collection collection, CancellationToken cancellationToken = default) => throw new NotImplementedException();
}
