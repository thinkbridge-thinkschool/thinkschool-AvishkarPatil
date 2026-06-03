using Azure.Identity;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Extensions;
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

if (app.Environment.IsEnvironment("Testing"))
{
    app.MapGet("/test/crash",  ()  => { throw new InvalidOperationException("deliberate crash"); });
    app.MapGet("/test/cancel", ()  => { throw new OperationCanceledException(); });
}

app.Run();

public partial class Program { }
