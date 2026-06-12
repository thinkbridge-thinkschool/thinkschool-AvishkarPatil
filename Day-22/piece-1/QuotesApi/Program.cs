using Azure.Identity;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Extensions;
using QuotesApi.Messaging;
using QuotesApi.Middleware;
using QuotesApi.Resilience;
using QuotesApi.Services;
using Serilog;
using Serilog.Context;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

var keyVaultUri = builder.Configuration["KeyVault:Uri"];
if (!string.IsNullOrWhiteSpace(keyVaultUri)
    && !builder.Environment.IsEnvironment("Testing"))
{
    builder.Configuration.AddAzureKeyVault(
        new Uri(keyVaultUri),
        new DefaultAzureCredential());
}

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddProblemDetails();

var app = builder.Build();

app.Use(async (ctx, next) =>
{
    using (LogContext.PushProperty("TraceId", ctx.TraceIdentifier))
    {
        await next();
    }
});

app.UseSerilogRequestLogging();

app.UseMiddleware<ExceptionMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    // Day-11: the perf demo runs against SQL Server (Day-4 added the provider).
    // The committed SQLite-based migrations would not apply, so for SQL Server
    // we EnsureCreated() from the EF model directly; Week-1 SQLite still uses
    // migrations.
    var provider = app.Configuration["Database:Provider"] ?? "Sqlite";
    if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
        db.Database.EnsureCreated();
    else
        db.Database.Migrate();

    await DbSeeder.SeedAsync(db);

    // Top up with 500 quotes + 5 collections × 20 items if the perf exercise
    // is enabled.  Idempotent — skipped once the targets are reached.
    if (app.Configuration.GetValue<bool>("PerfDemo:SeedPerfData"))
        await DbSeeder.SeedPerfDataAsync(db);
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
   .AllowAnonymous();

// Diagnostic: forces the resilient HttpClient to fetch Entra ID's OIDC metadata.
// Useful for manually proving the Polly pipeline is wired (you can yank the network
// mid-call and watch the retry log lines fly past).
app.MapGet("/diagnostics/entra-id-metadata",
    async (QuotesApi.Services.IEntraIdMetadataClient client, CancellationToken ct) =>
    {
        var doc = await client.GetDiscoveryDocumentAsync(ct);
        var issuer = doc.TryGetProperty("issuer", out var i) ? i.GetString() : null;
        return Results.Ok(new { issuer });
    })
   .AllowAnonymous();

app.MapAuthEndpoints();
app.MapQuoteEndpoints();
app.MapCollectionCqrsEndpoints();   // Day-12 piece-1: CQRS-lite read/write split
app.MapCollectionPerfEndpoints();   // Day-11: /slow + /fast profiling endpoints

// ── Day-19: Service Bus demo endpoints ───────────────────────────────────────
// These are unauthenticated for exercise convenience; in production they would
// require at minimum the "can-edit-quotes" policy.

// Publish a real QuoteCreated message — fans out to both subscriptions.
app.MapPost("/api/service-bus/publish", async (
    IQuotePublisher publisher,
    CancellationToken ct) =>
{
    var msg = new QuoteCreatedMessage(
        QuoteId:   99999,
        Author:    "Demo Author",
        Text:      "Published directly to Service Bus for Day-19 demo.",
        CreatedAt: DateTimeOffset.UtcNow);

    await publisher.PublishAsync(msg, ct);
    return Results.Ok(new { published = true, quoteId = msg.QuoteId });
}).AllowAnonymous();

// Test endpoint: publish with an explicit MessageId so the duplicate scenario can be
// reproduced deterministically (call twice with the same id, second must be skipped).
app.MapPost("/api/service-bus/publish-with-id/{messageId}", async (
    string          messageId,
    IQuotePublisher publisher,
    CancellationToken ct) =>
{
    await publisher.PublishWithIdAsync(messageId, ct);
    return Results.Ok(new { published = true, messageId });
}).AllowAnonymous();

// Publish a poison message — triggers retries then DLQ on analytics-subscription.
app.MapPost("/api/service-bus/publish-poison", async (
    IQuotePublisher publisher,
    CancellationToken ct) =>
{
    await publisher.PublishPoisonAsync(ct);
    return Results.Ok(new
    {
        published = true,
        note = "Poison message published. Watch retries in logs, then check DLQ."
    });
}).AllowAnonymous();

// ── Day-20: Outbox monitoring endpoints ──────────────────────────────────────
// GET /api/outbox/pending   — shows rows not yet relayed to Service Bus.
//   Useful for verifying crash-safety: create a quote, then query this endpoint
//   before the relay's next poll cycle; you'll see the unsent row.  After the
//   relay runs, the row disappears from this list (ProcessedAt is now set).
//
// POST /api/outbox/simulate-crash — writes an outbox row WITHOUT publishing, to
//   simulate "app crashed between DB commit and Service Bus send".  On the next
//   relay cycle the row is picked up and published — demonstrating crash recovery.

app.MapGet("/api/outbox/pending", async (AppDbContext db, CancellationToken ct) =>
{
    var pending = await db.OutboxMessages
        .Where(m => m.ProcessedAt == null)
        .OrderBy(m => m.CreatedAt)
        .Select(m => new
        {
            m.Id,
            m.MessageType,
            m.MessageId,
            m.CreatedAt,
            m.Error
        })
        .ToListAsync(ct);

    return Results.Ok(new { count = pending.Count, messages = pending });
}).AllowAnonymous();

app.MapPost("/api/outbox/simulate-crash", async (AppDbContext db, CancellationToken ct) =>
{
    // Simulate a crash scenario: persist an outbox row directly (bypassing the
    // normal Quote creation flow) without calling the publisher.  This is exactly
    // the state the database would be in if the application crashed after the DB
    // transaction committed but before Service Bus was called.
    // The OutboxRelayWorker will find this row on the next poll cycle and publish it.
    var fakeMsg = new QuoteCreatedMessage(
        QuoteId:   0,
        Author:    "Crash Simulation",
        Text:      "This row was written to simulate a crash before Service Bus publish.",
        CreatedAt: DateTimeOffset.UtcNow);

    var outbox = OutboxMessage.Create(
        messageType: "QuoteCreated",
        payload:     JsonSerializer.Serialize(fakeMsg));

    db.OutboxMessages.Add(outbox);
    await db.SaveChangesAsync(ct);

    return Results.Ok(new
    {
        note       = "Outbox row persisted. The relay will publish it within the next poll cycle (~10 s).",
        outboxId   = outbox.Id,
        messageId  = outbox.MessageId,
        createdAt  = outbox.CreatedAt
    });
}).AllowAnonymous();

if (app.Environment.IsEnvironment("Testing"))
{
    app.MapGet("/test/crash",  ()  => { throw new InvalidOperationException("deliberate crash"); });
    app.MapGet("/test/cancel", ()  => { throw new OperationCanceledException(); });
}

// ── Day-22: Polly resilience demo endpoints ───────────────────────────────────
//
// These endpoints are intentionally unauthenticated — they exist solely to
// demonstrate the Polly pipeline for the exercise.  Remove or protect them in
// any production deployment.

// Enable fault injection: every outbound Entra-ID HTTP call will return 503.
// The Polly pipeline then fires: retry × 3 → circuit-breaker failure count rises.
app.MapPost("/api/resilience/fault/enable", () =>
{
    FaultInjectionHandler.Enabled = true;
    return Results.Ok(new
    {
        fault  = true,
        note   = "Fault injection ON — every Entra-ID call will return 503. " +
                 "Fire GET /diagnostics/entra-id-metadata or POST /api/resilience/hammer/{n} " +
                 "to accumulate failures and open the circuit."
    });
}).AllowAnonymous();

// Disable fault injection: real HTTP calls resume.
// After BreakDuration (15 s) the circuit transitions OPEN → HALF-OPEN.
// The next successful probe closes it: HALF-OPEN → CLOSED.
app.MapPost("/api/resilience/fault/disable", () =>
{
    FaultInjectionHandler.Enabled = false;
    return Results.Ok(new
    {
        fault = false,
        note  = "Fault injection OFF — real HTTP calls resume. " +
                "Wait ~15 s for BreakDuration then call GET /diagnostics/entra-id-metadata " +
                "to trigger the HALF-OPEN probe and close the circuit."
    });
}).AllowAnonymous();

// Fire `count` consecutive calls through the resilient EntraIdMetadataClient.
// With fault injection enabled, each call triggers retries and increments the
// circuit-breaker failure count.  After MinimumThroughput (5) failures at
// ≥ 50 % failure ratio the circuit opens — subsequent hammer calls fail fast
// with BrokenCircuitException (no retries, no network).
app.MapPost("/api/resilience/hammer/{count:int}",
    async (int count, IEntraIdMetadataClient client, CancellationToken ct) =>
    {
        if (count is < 1 or > 50)
            return Results.BadRequest(new { error = "count must be between 1 and 50" });

        var results = new List<object>();
        for (var i = 0; i < count; i++)
        {
            try
            {
                await client.GetDiscoveryDocumentAsync(ct);
                results.Add(new { attempt = i + 1, outcome = "success" });
            }
            catch (Exception ex)
            {
                results.Add(new
                {
                    attempt = i + 1,
                    outcome = ex.GetType().Name,
                    message = ex.Message
                });
            }
        }

        return Results.Ok(new
        {
            requested = count,
            completed = results.Count,
            metrics   = new
            {
                retryAttempts    = ResilienceMetrics.RetryAttemptCount,
                timeouts         = ResilienceMetrics.TimeoutCount,
                circuitOpened    = ResilienceMetrics.CircuitOpenedCount,
                bulkheadRejected = ResilienceMetrics.BulkheadRejectedCount,
            },
            results
        });
    }).AllowAnonymous();

// Live resilience counters + fault-mode state.
// Poll this while running the hammer to watch the circuit open and recover.
app.MapGet("/api/resilience/status", () =>
    Results.Ok(new
    {
        faultInjection = FaultInjectionHandler.Enabled,
        metrics        = new
        {
            retryAttempts    = ResilienceMetrics.RetryAttemptCount,
            timeouts         = ResilienceMetrics.TimeoutCount,
            circuitOpened    = ResilienceMetrics.CircuitOpenedCount,
            bulkheadRejected = ResilienceMetrics.BulkheadRejectedCount,
        }
    })).AllowAnonymous();

app.Run();

public partial class Program { }
