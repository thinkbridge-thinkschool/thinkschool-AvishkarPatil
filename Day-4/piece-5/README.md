# Day 4 · Piece 5 — OpenTelemetry tracing

Add **distributed tracing** to the Quotes API. Every HTTP request becomes a trace with nested spans for each EF Core query, every outbound `HttpClient` call, and any custom operation I wrap. The same `TraceId` Serilog already emits is now the OTel `trace_id` — logs and traces correlate without any extra plumbing.

---

## OTel setup

### 1. Packages — [QuotesApi.csproj](QuotesApi.csproj)

```xml
<PackageReference Include="OpenTelemetry.Extensions.Hosting"               Version="1.10.0" />
<PackageReference Include="OpenTelemetry.Instrumentation.AspNetCore"       Version="1.10.1" />
<PackageReference Include="OpenTelemetry.Instrumentation.EntityFrameworkCore" Version="1.10.0-beta.1" />
<PackageReference Include="OpenTelemetry.Instrumentation.Http"             Version="1.10.0" />
<PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol"   Version="1.10.0" />
```

### 2. Wire-up — [Extensions/InfrastructureExtensions.cs](Extensions/InfrastructureExtensions.cs)

```csharp
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using QuotesApi.Observability;

services
    .AddOpenTelemetry()
    .ConfigureResource(r => r.AddService(QuotesTelemetry.ServiceName))
    .WithTracing(t => t
        .AddSource(QuotesTelemetry.ServiceName)
        .AddAspNetCoreInstrumentation()
        .AddEntityFrameworkCoreInstrumentation(o => o.SetDbStatementForText = true)
        .AddHttpClientInstrumentation()
        .AddOtlpExporter());
```

What each line does:

- `AddService("QuotesApi")` — every span is stamped with `service.name = QuotesApi`, so Jaeger/Aspire can group them under a service.
- `AddSource("QuotesApi")` — tells OTel to listen to the custom `ActivitySource` I create below. Without this, my custom spans are dropped.
- `AddAspNetCoreInstrumentation()` — root span per HTTP request, with method, route, status code, and duration.
- `AddEntityFrameworkCoreInstrumentation(SetDbStatementForText = true)` — one child span per EF query, with the SQL text attached.
- `AddHttpClientInstrumentation()` — one child span per outbound `HttpClient` call, with the W3C `traceparent` header injected so the next service joins the same trace.
- `AddOtlpExporter()` — ships spans over OTLP/gRPC to `http://localhost:4317` by default. Overridable with `OTEL_EXPORTER_OTLP_ENDPOINT`.

### 3. Shared `ActivitySource` — [Observability/QuotesTelemetry.cs](Observability/QuotesTelemetry.cs)

```csharp
using System.Diagnostics;

namespace QuotesApi.Observability;

public static class QuotesTelemetry
{
    public const string ServiceName = "QuotesApi";

    public static readonly ActivitySource Source = new(ServiceName);
}
```

One static `ActivitySource` per service is the convention — the same string is registered with `.AddSource(...)` above and used to start spans below.

### 4. Custom span — [Extensions/QuoteEndpointExtensions.cs](Extensions/QuoteEndpointExtensions.cs)

`POST /api/quotes` does a few things automatic instrumentation can't see on its own (claim parsing, domain object construction). Wrapping it in a custom span makes that work visible as a child of the request span:

```csharp
group.MapPost("/", async (
    CreateQuoteRequest request,
    ClaimsPrincipal user,
    IQuoteRepository repository,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    using var activity = QuotesTelemetry.Source.StartActivity("create-quote");

    // ...
    activity?.SetTag("user.id", ownerId);
    activity?.SetTag("quote.author", request.Author);
    activity?.SetTag("quote.text.length", request.Text?.Length ?? 0);

    var quote   = Quote.Create(request.Author, request.Text, ownerId);
    var created = await repository.CreateAsync(quote, cancellationToken);

    activity?.SetTag("quote.id", created.Id);

    return Results.Created($"/api/quotes/{created.Id}", created);
}).RequireAuthorization("can-edit-quotes");
```

The `?.` is deliberate — when no listener is registered (e.g. a unit test where OTel isn't wired up) `StartActivity` returns `null` and the tags are no-ops, so the code stays cheap to call.

---

## Running a collector

The OTLP exporter targets `localhost:4317` (gRPC) by default. I used **Jaeger all-in-one**, which has accepted OTLP natively since 1.42:

```bash
docker run --rm -d --name jaeger \
  -e COLLECTOR_OTLP_ENABLED=true \
  -p 16686:16686 \
  -p 4317:4317 \
  -p 4318:4318 \
  jaegertracing/all-in-one:latest
```

Then start the API and hit it:

```bash
dotnet run --project QuotesApi.csproj
# in another shell:
curl -X POST http://localhost:5000/api/quotes \
     -H "Authorization: Bearer <token>" \
     -H "Content-Type: application/json" \
     -d '{"author":"Seneca","text":"Luck is what happens when preparation meets opportunity."}'
```

Open the Jaeger UI at <http://localhost:16686>, pick service **QuotesApi**, hit Find Traces.

### Screenshot

A trace for `POST /api/quotes/` showing nested spans — the AspNetCore root, the custom `create-quote` span, and the EF Core `INSERT` underneath:

![Jaeger trace](docs/jaeger-trace.png)

---

## Log / trace correlation — free

`Microsoft.Extensions.Logging` already enriches every log event with `Activity.Current.TraceId` and `SpanId` when one is active. Because OTel sets `Activity.Current` for the duration of the request, Serilog's `Enrich.FromLogContext()` picks them up automatically:

```
[11:14:02 INF] Creating quote for user 1 by author Seneca
  { "TraceId": "5a3f1cf8b27e4a90e7b1cc62a7e1ad34", "SpanId": "9e3c8b2a8d7f4e21", ... }
```

That `TraceId` is the *same string* I now see in Jaeger's URL. Click a log line → paste TraceId into Jaeger → see the full request including DB spans. Click a span in Jaeger → grep your log sink for that TraceId → see every structured log raised inside it. No glue code.

---

## Exercise reflection

### Q1 — What did you learn this session?

The thing that clicked for me is that **a trace is just a tree of timed operations, and most of that tree gets built for you for free.** I added four `.Add...Instrumentation()` calls and suddenly I have a span per HTTP request, a span per EF query, a span per outbound HTTP call, and they all nest correctly by parent-child relationship — no manual wiring, no IDs threaded through method signatures. The piece that finishes the picture is the **shared `ActivitySource`**: my own non-trivial operations (the `create-quote` endpoint) become spans the same way EF queries do, and they slot in as children of the request span automatically because `Activity.Current` is already set. The other thing I'll keep is that **logs and traces correlating is not a feature I built, it's a side-effect of OTel setting `Activity.Current` and Serilog's `LogContext` enricher reading from it.** I delete a custom TraceId middleware and I get *better* correlation than before, because the trace ID is now a real W3C `trace_id` that propagates across services via the `traceparent` header — not a per-process Kestrel counter that breaks at the first load balancer.

### Q2 — What would break this?

The most likely failure mode is the **performance/cost shape of `SetDbStatementForText = true`** in production. Right now I'm shipping the full SQL text of every EF query — including parameter placeholders — to the collector. On a hot read path that's both a lot of network bytes and a real PII risk if the query embeds user input as a literal (which it shouldn't, with EF parameters, but a raw SQL escape hatch elsewhere in the codebase would leak). The fix is to keep statement capture on in dev only, off in prod, and to add a **sampler** (`SetSampler(new TraceIdRatioBasedSampler(0.1))` or a parent-based sampler that follows the inbound `traceparent`) so I'm not paying to emit a span for every health check. The second failure mode is **the OTLP exporter has no collector to talk to.** The default exporter is fire-and-forget over gRPC; if Jaeger is down it queues, retries with backoff, and eventually drops spans, but it does *not* crash my API or block requests. That's good for prod, but in dev it means I can spend ten minutes wondering why no traces show up before noticing my Docker container died. The third one — much subtler — is **a span I forget to dispose.** `using var activity = ...` is the idiom for a reason: if I store the `Activity` and forget to call `Stop()` or `Dispose()`, the span never ends, never exports, and worse, becomes the `Activity.Current` for whatever async work runs after it — so the *next* request's spans nest under the wrong parent. Always `using`.

---

## Links

- **Repository:** [https://github.com/thinkbridge-thinkschool/thinkschool-AvishkarPatil](https://github.com/thinkbridge-thinkschool/thinkschool-AvishkarPatil)
- **Folder:** [Day-4/piece-5](https://github.com/thinkbridge-thinkschool/thinkschool-AvishkarPatil/tree/main/Day-4/piece-5)
