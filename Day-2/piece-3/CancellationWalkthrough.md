# Cancellation Flow in Collection Endpoints

Here are the components that make cancellation flow all the way through, along with the test that proves it. 

## 1. The Endpoint Mappings
The Minimal API automatically binds the `CancellationToken` (which reflects the HTTP Request Abort signal) if it is specified in the delegate signature. We pass it down to our repository.

```csharp
// Extensions/CollectionEndpointExtensions.cs
public static class CollectionEndpointExtensions
{
    public static IEndpointRouteBuilder MapCollectionEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/collections");

        group.MapPost("/", async (
            CreateCollectionRequest request,
            ICollectionRepository repository,
            CancellationToken cancellationToken) => // Token bound automatically
        {
            var collection = new Collection(request.Name, request.OwnerId);

            // Flow the token to the repository layer
            var created = await repository.CreateAsync(collection, cancellationToken);

            return Results.Created($"/api/collections/{created.Id}", created);
        });

        // Other endpoints ...

        return app;
    }
}
```

## 2. The Repository Implementation
EF Core perfectly supports cancellation. We pass the token all the way into `SaveChangesAsync`.

```csharp
// Repositories/CollectionRepository.cs
public class CollectionRepository : ICollectionRepository
{
    private readonly AppDbContext _context;

    public CollectionRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<Collection> CreateAsync(Collection collection, CancellationToken cancellationToken = default)
    {
        _context.Collections.Add(collection);
        
        // Pass the token to the actual I/O operation
        await _context.SaveChangesAsync(cancellationToken);
        return collection;
    }

    // Other methods...
}
```

## 3. The Exception Middleware
When a request is cancelled during I/O, an `OperationCanceledException` (or `TaskCanceledException`) is thrown. We handle it globally to return HTTP status 499 (Client Closed Request) instead of logging a massive 500 error stack trace.

```csharp
// Middleware/ExceptionMiddleware.cs
public async Task InvokeAsync(HttpContext context)
{
    try
    {
        await _next(context);
    }
    catch (OperationCanceledException)
    {
        _logger.LogInformation("Request was cancelled by the client.");
        context.Response.StatusCode = 499; // 499 Client Closed Request
    }
    catch (DomainException ex)
    {
        // 400 Bad Request...
    }
    catch (Exception ex)
    {
        // 500 Server Error...
    }
}
```

## 4. The Cancellation Test
This integration test completely overrides the repository with a mocked `DelayingCollectionRepository` that guarantees we delay longer than the test's cancellation timeout. It verifies both that the client connection aborts, and that the cancellation signal specifically reached the innermost database logic.

```csharp
// QuotesApi.Tests/CancellationTests.cs
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
                // Register a fake repository that delays and respects cancellation
                services.AddScoped<ICollectionRepository>(sp => new DelayingCollectionRepository(
                    onCancelled: () => isCancelledInRepository = true
                ));
            });
        });

        var client = factory.CreateClient();
        var cts = new CancellationTokenSource();

        var request = new CreateCollectionRequest("Test Collection", "user123");
        var content = JsonContent.Create(request);

        // Cancel after 500ms
        cts.CancelAfter(TimeSpan.FromMilliseconds(500));

        // Act & Assert
        // The TestServer client will throw when the underlying request aborts
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await client.PostAsync("/api/collections", content, cts.Token);
        });

        // Wait a bit to ensure the background task had time to process the cancellation
        await Task.Delay(500);
        
        // Assert: The token must have been successfully passed down to the repository layer
        Assert.True(isCancelledInRepository, "Cancellation did not flow to the repository layer.");
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
            // Delay for 2 seconds to ensure our 500ms cancellation triggers while waiting
            await Task.Delay(2000, cancellationToken);
            return collection;
        }
        catch (OperationCanceledException)
        {
            _onCancelled();
            throw; // Re-throw to bubble up to our middleware
        }
    }

    public Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<Collection?> GetByIdAsync(int id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<bool> UpdateAsync(Collection collection, CancellationToken cancellationToken = default) => throw new NotImplementedException();
}
```
