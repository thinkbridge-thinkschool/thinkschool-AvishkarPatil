using System.Net;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using QuotesApi.Extensions;

namespace Quotes.Tests.Unit;

/// <summary>
/// Exercises the production resilience pipeline against a stub HttpMessageHandler
/// that returns transient 503s before succeeding, and proves that:
///   - the retry strategy fires the expected number of times
///   - every retry produces a structured "Polly retry …" log line
///   - the final response succeeds (no exception leaked to the caller)
/// </summary>
public class HttpResilienceTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;
    public HttpResilienceTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Returns_503_twice_then_200_should_trigger_two_retries_and_succeed()
    {
        var stub = new ScriptedHandler(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            new HttpResponseMessage(HttpStatusCode.OK));

        var sink = new InMemoryLogProvider();

        var services = new ServiceCollection();
        services.AddLogging(b =>
        {
            b.SetMinimumLevel(LogLevel.Debug);
            b.AddProvider(sink);
        });

        services.AddHttpClient("entra-id")
            .ConfigurePrimaryHttpMessageHandler(() => stub)
            .AddResilienceHandler("default", (pipeline, ctx) =>
            {
                var logger = ctx.ServiceProvider
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("HttpResilience.entra-id");

                pipeline.AddDefaultResiliencePipeline(logger, pipelineName: "entra-id");
            });

        await using var sp = services.BuildServiceProvider();
        var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("entra-id");
        http.BaseAddress = new Uri("https://login.microsoftonline.com/common/v2.0/");

        var response = await http.GetAsync(".well-known/openid-configuration");

        foreach (var e in sink.Entries)
            _output.WriteLine($"[{e.LogLevel,-11}] {e.Category}: {e.Message}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        stub.Calls.Should().Be(3, "1 initial attempt + 2 retries before the 200");

        var retryLines = sink.Entries
            .Where(e => e.Message.StartsWith("Polly retry", StringComparison.Ordinal))
            .ToList();

        retryLines.Should().HaveCount(2, "two transient failures should each produce a retry log line");
        retryLines.Should().AllSatisfy(e =>
        {
            e.LogLevel.Should().Be(LogLevel.Warning);
            e.Message.Should().Contain("HTTP 503");
            e.Message.Should().Contain("pipeline entra-id");
        });
    }

    [Fact]
    public async Task Persistent_failures_should_exhaust_retries_and_surface_the_last_failure()
    {
        var stub = new ScriptedHandler(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var sink = new InMemoryLogProvider();

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(sink));
        services.AddHttpClient("entra-id")
            .ConfigurePrimaryHttpMessageHandler(() => stub)
            .AddResilienceHandler("default", (pipeline, ctx) =>
            {
                var logger = ctx.ServiceProvider
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("HttpResilience.entra-id");

                pipeline.AddDefaultResiliencePipeline(logger, pipelineName: "entra-id");
            });

        await using var sp = services.BuildServiceProvider();
        var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("entra-id");
        http.BaseAddress = new Uri("https://login.microsoftonline.com/common/v2.0/");

        var response = await http.GetAsync(".well-known/openid-configuration");

        foreach (var e in sink.Entries)
            _output.WriteLine($"[{e.LogLevel,-11}] {e.Category}: {e.Message}");

        // After 3 retries the pipeline gives up and returns the last 503 to the caller.
        // The failure is not swallowed: the caller still sees a non-success status.
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        stub.Calls.Should().Be(4, "1 initial attempt + 3 retries = 4 total calls");

        var retryLines = sink.Entries
            .Count(e => e.Message.StartsWith("Polly retry", StringComparison.Ordinal));
        retryLines.Should().Be(3);
    }

    // --- helpers --------------------------------------------------------

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;
        public int Calls { get; private set; }

        public ScriptedHandler(params HttpResponseMessage[] responses)
            => _responses = new Queue<HttpResponseMessage>(responses);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            var next = _responses.Count > 0
                ? _responses.Dequeue()
                : new HttpResponseMessage(HttpStatusCode.OK);
            return Task.FromResult(next);
        }
    }

    private sealed record LogEntry(LogLevel LogLevel, string Category, string Message);

    private sealed class InMemoryLogProvider : ILoggerProvider
    {
        public List<LogEntry> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new InMemoryLogger(categoryName, Entries);
        public void Dispose() { }

        private sealed class InMemoryLogger : ILogger
        {
            private readonly string _category;
            private readonly List<LogEntry> _sink;

            public InMemoryLogger(string category, List<LogEntry> sink)
            {
                _category = category;
                _sink     = sink;
            }

            public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (_sink)
                {
                    _sink.Add(new LogEntry(logLevel, _category, formatter(state, exception)));
                }
            }

            private sealed class NullScope : IDisposable
            {
                public static readonly NullScope Instance = new();
                public void Dispose() { }
            }
        }
    }
}
