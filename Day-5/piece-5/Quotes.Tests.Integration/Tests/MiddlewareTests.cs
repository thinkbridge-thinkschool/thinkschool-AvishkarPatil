using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Quotes.Tests.Integration.Infrastructure;

namespace Quotes.Tests.Integration.Tests;

/// <summary>
/// Verifies that ExceptionMiddleware translates uncaught exceptions into
/// the correct HTTP status codes and ProblemDetails bodies.
/// </summary>
[Collection("SqlServer")]
public class MiddlewareTests : IntegrationTestBase
{
    public MiddlewareTests(SqlServerContainerFixture fixture) : base(fixture) { }

    [Fact]
    public async Task UnhandledException_Returns500WithProblemDetails()
    {
        var response = await Client.GetAsync("/test/crash");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("status").GetInt32().Should().Be(500);
        problem.GetProperty("title").GetString().Should().Be("Server Error");
    }

    [Fact]
    public async Task OperationCanceledException_Returns499()
    {
        var response = await Client.GetAsync("/test/cancel");

        ((int)response.StatusCode).Should().Be(499,
            "ExceptionMiddleware maps OperationCanceledException to 499 Client Closed Request");
    }
}
