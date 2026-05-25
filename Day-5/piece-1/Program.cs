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
    db.Database.Migrate();
    await DbSeeder.SeedAsync(db);
}

app.MapAuthEndpoints();
app.MapQuoteEndpoints();
app.MapCollectionEndpoints();

if (app.Environment.IsEnvironment("Testing"))
{
    app.MapGet("/test/crash",  ()  => { throw new InvalidOperationException("deliberate crash"); });
    app.MapGet("/test/cancel", ()  => { throw new OperationCanceledException(); });
}

app.Run();

public partial class Program { }
