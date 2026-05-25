# Day 4 · Piece 4 — Serilog with Correlation IDs

Replace the default logger with **Serilog**, force every log call to be **structured** (named properties, not interpolated strings), and stamp every line in a request with the same **TraceId** so logs from one HTTP call can be filtered as a group.

---

## Serilog setup

### 1. Packages — [QuotesApi.csproj](QuotesApi.csproj)

```xml
<PackageReference Include="Serilog.AspNetCore" Version="9.0.0" />
<PackageReference Include="Serilog.Sinks.Console" Version="6.0.0" />
<PackageReference Include="Serilog.Settings.Configuration" Version="9.0.0" />
```

### 2. Wire-up — [Program.cs](Program.cs)

```csharp
using Serilog;
using Serilog.Context;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

// ...

var app = builder.Build();

app.Use(async (ctx, next) =>
{
    using (LogContext.PushProperty("TraceId", ctx.TraceIdentifier))
    {
        await next();
    }
});

app.UseSerilogRequestLogging();
```

Two things are doing the work:

- `Enrich.FromLogContext()` tells Serilog to attach any property pushed onto `LogContext` to every log event raised on the same async-flow.
- The tiny middleware pushes `TraceId = ctx.TraceIdentifier` once per request. From that line onward — through the endpoint, the repository, EF Core, and the `UseSerilogRequestLogging` completion line — every event carries the same `TraceId`.

`UseSerilogRequestLogging` adds the standard “HTTP POST /api/quotes/ responded 201 in 135ms” line at the end of each request.

### 3. Levels per category — [appsettings.json](appsettings.json) / [appsettings.Development.json](appsettings.Development.json)

```jsonc
// appsettings.json — production defaults
"Serilog": {
  "MinimumLevel": {
    "Default": "Information",
    "Override": {
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore": "Warning",
      "Microsoft.EntityFrameworkCore.Database.Command": "Warning"
    }
  },
  "WriteTo": [
    {
      "Name": "Console",
      "Args": {
        "outputTemplate": "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}"
      }
    }
  ],
  "Enrich": [ "FromLogContext" ]
}
```

```jsonc
// appsettings.Development.json — verbose SQL only in dev
"Serilog": {
  "MinimumLevel": {
    "Default": "Information",
    "Override": {
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore.Database.Command": "Debug"
    }
  }
}
```

Microsoft’s framework logs sit at `Warning`, my own code at `Information`, and EF Core SQL text drops to `Debug` only in dev.

### 4. Structured calls — [Extensions/QuoteEndpointExtensions.cs](Extensions/QuoteEndpointExtensions.cs), [Repositories/QuoteRepository.cs](Repositories/QuoteRepository.cs)

Every log call uses a message template with named holes, never an interpolated string:

```csharp
logger.LogInformation(
    "Creating quote for user {UserId} by author {Author}",
    ownerId, request.Author);

// ...

logger.LogInformation(
    "Persisted quote {QuoteId} for user {UserId}",
    created.Id, ownerId);
```

That makes `UserId`, `Author`, and `QuoteId` queryable fields in the sink — not characters glued into a free-form sentence.

---

## 5 lines from a single request

`POST /api/quotes/` with the demo writer’s token. Every line below — emitted from the endpoint, the repository, and Serilog’s request-logging middleware — carries the same `TraceId: 0HNLQ410JLTGC:00000001`:

```
[11:11:40 INF] Creating quote for user 1 by author Seneca {"SourceContext": "QuotesApi.Quotes", "RequestId": "0HNLQ410JLTGC:00000001", "RequestPath": "/api/quotes/", "ConnectionId": "0HNLQ410JLTGC", "TraceId": "0HNLQ410JLTGC:00000001"}
[11:11:40 INF] Quote built in memory with author Seneca and length 56 {"SourceContext": "QuotesApi.Quotes", "RequestId": "0HNLQ410JLTGC:00000001", "RequestPath": "/api/quotes/", "ConnectionId": "0HNLQ410JLTGC", "TraceId": "0HNLQ410JLTGC:00000001"}
[11:11:40 INF] Repository saved 1 row(s) for quote 2 {"SourceContext": "QuotesApi.Repositories.QuoteRepository", "RequestId": "0HNLQ410JLTGC:00000001", "RequestPath": "/api/quotes/", "ConnectionId": "0HNLQ410JLTGC", "TraceId": "0HNLQ410JLTGC:00000001"}
[11:11:40 INF] Persisted quote 2 for user 1 {"SourceContext": "QuotesApi.Quotes", "RequestId": "0HNLQ410JLTGC:00000001", "RequestPath": "/api/quotes/", "ConnectionId": "0HNLQ410JLTGC", "TraceId": "0HNLQ410JLTGC:00000001"}
[11:11:40 INF] HTTP POST /api/quotes/ responded 201 in 135.4167 ms {"SourceContext": "Serilog.AspNetCore.RequestLoggingMiddleware", "TraceId": "0HNLQ410JLTGC:00000001", "RequestId": "0HNLQ410JLTGC:00000001", "ConnectionId": "0HNLQ410JLTGC"}
```

In a real sink (Seq, App Insights, Loki) the `TraceId` field is indexed, so `TraceId = "0HNLQ410JLTGC:00000001"` returns exactly this slice of the system’s activity — endpoint, repository, and HTTP-completion line — for one request.

---

## Exercise reflection

### Q1 — What did you learn this session?

The thing that clicked for me is that a log line is a **piece of data**, not a sentence. `LogInformation("Created quote {QuoteId} for user {UserId}", quoteId, userId)` and `LogInformation($"Created quote {quoteId} for user {userId}")` look almost identical to the human reading the source, but they are completely different to the machine reading the sink: the first stores `QuoteId` and `UserId` as named properties you can filter on, the second stores one opaque string you can only grep. The second hidden lesson is that **correlation is free if you set it up once.** `Enrich.FromLogContext()` plus a one-line middleware that pushes `TraceId = ctx.TraceIdentifier` means every log event raised anywhere in a request — endpoint code, repository code, framework code — automatically carries the same TraceId. I don’t have to remember to thread a request ID through every method signature; the async-flow context does it for me. The idea I’ll keep: **logs are queries waiting to happen**, and the time to pick the property name is at the call site, not after an outage.

### Q2 — What would break this?

The brittlest part is `ctx.TraceIdentifier`. It’s only unique **inside one Kestrel process** — restart the API, deploy a new pod, scale out to a second instance, and the same TraceId string can repeat across days and machines. The moment a request crosses a boundary (load balancer → API → background job → another service) my correlation is also gone, because nothing on the other side knows about that ID. The fix is to read an inbound header like `traceparent` (W3C Trace Context) when one is present, generate a real Activity ID otherwise, and propagate that ID outward in every outbound HTTP call and queue message — not just rely on Kestrel’s per-process counter. The other failure mode is anyone slipping in a `$"..."` log call: it compiles, it looks fine in the console output, and it silently destroys searchability for that one line. A Roslyn analyzer or a code-review checklist is what keeps that from rotting the corpus over time.

---

## Links

- **Repository:** [https://github.com/thinkbridge-thinkschool/thinkschool-AvishkarPatil](https://github.com/thinkbridge-thinkschool/thinkschool-AvishkarPatil)
- **Folder:** [Day-4/piece-4](https://github.com/thinkbridge-thinkschool/thinkschool-AvishkarPatil/tree/main/Day-4/piece-4)
