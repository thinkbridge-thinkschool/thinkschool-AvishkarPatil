using Azure.Identity;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Extensions;
using QuotesApi.Messaging;
using QuotesApi.Middleware;
using Serilog;
using Serilog.Context;

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

if (app.Environment.IsEnvironment("Testing"))
{
    app.MapGet("/test/crash",  ()  => { throw new InvalidOperationException("deliberate crash"); });
    app.MapGet("/test/cancel", ()  => { throw new OperationCanceledException(); });
}

app.Run();

public partial class Program { }
