using System.Diagnostics;

namespace QuotesApi.Observability;

public static class QuotesTelemetry
{
    public const string ServiceName = "QuotesApi";

    public static readonly ActivitySource Source = new(ServiceName);
}
