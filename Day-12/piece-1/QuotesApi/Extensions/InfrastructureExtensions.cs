using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using QuotesApi.Application.Commands.Collections;
using QuotesApi.Application.Queries.Collections;
using QuotesApi.Authorization;
using QuotesApi.Configuration;
using QuotesApi.Data;
using QuotesApi.Observability;
using QuotesApi.Repositories;
using QuotesApi.Services;

namespace QuotesApi.Extensions;

public static class InfrastructureExtensions
{
    private const string InternalScheme = "Internal";
    private const string EntraScheme   = "EntraId";
    private const string MultiScheme   = "MultiAuth";

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString("Default");
            var provider         = configuration["Database:Provider"] ?? "Sqlite";

            if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
                options.UseSqlServer(connectionString);
            else
                options.UseSqlite(connectionString);

            // Day-11 perf instrumentation: pipe EF Core's generated SQL straight to the
            // console so the offending statements can be captured for the deliverable.
            // Off by default (Week-1 behaviour); turned on via Database:LogSql=true.
            if (configuration.GetValue<bool>("Database:LogSql"))
            {
                options
                    .LogTo(
                        msg => Console.WriteLine(msg),
                        new[] { DbLoggerCategory.Database.Command.Name },
                        LogLevel.Information)
                    .EnableSensitiveDataLogging();
            }
        });

        services.AddScoped<IQuoteRepository, QuoteRepository>();
        services.AddScoped<ICollectionRepository, CollectionRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<ITokenService, TokenService>();

        // ── Day-12 piece-1 — CQRS-lite ───────────────────────────────────
        // Query service (reads) lives next to the repositories (writes).
        // Both are scoped per HTTP request so they share the same DbContext.
        services.AddScoped<ICollectionQueryService, CollectionQueryService>();

        // Command handlers — registered as scoped so they participate in
        // the request-scoped repository + DbContext lifecycle.
        services.AddScoped<CreateCollectionCommandHandler>();
        services.AddScoped<AddQuoteToCollectionCommandHandler>();

        services
            .AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services
            .AddOptions<EntraIdOptions>()
            .Bind(configuration.GetSection(EntraIdOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services
            .AddAuthentication(MultiScheme)
            .AddPolicyScheme(MultiScheme, "JWT – internal or Entra", options =>
            {
                // Peek at the issuer claim without validating the signature,
                // then forward to the scheme that owns that issuer.
                options.ForwardDefaultSelector = ctx =>
                {
                    var auth = ctx.Request.Headers.Authorization.FirstOrDefault();
                    if (auth?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        var raw     = auth["Bearer ".Length..].Trim();
                        var handler = new JwtSecurityTokenHandler();
                        if (handler.CanReadToken(raw))
                        {
                            var jwt = handler.ReadJwtToken(raw);
                            if (jwt.Issuer.Contains("microsoftonline.com",
                                    StringComparison.OrdinalIgnoreCase))
                                return EntraScheme;
                        }
                    }
                    return InternalScheme;
                };
            })
            .AddJwtBearer(InternalScheme, _ => { /* configured via IOptions<JwtOptions> below */ })
            .AddJwtBearer(EntraScheme,    _ => { /* configured via IOptions<EntraIdOptions> below */ });

        // Bridge: JwtBearerOptions per scheme are filled in from the bound JwtOptions/EntraIdOptions.
        // Doing it this way means the values used to sign tokens (TokenService) and the values used
        // to validate them (JwtBearer) come from the *same* IOptions instance — no second source of truth.
        services
            .AddOptions<JwtBearerOptions>(InternalScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
            {
                var j = jwt.Value;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer           = true,
                    ValidateAudience         = true,
                    ValidateLifetime         = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer              = j.Issuer,
                    ValidAudience            = j.Audience,
                    IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(j.Key)),
                    ClockSkew                = TimeSpan.Zero
                };
            });

        services
            .AddOptions<JwtBearerOptions>(EntraScheme)
            .Configure<IOptions<EntraIdOptions>>((bearer, entra) =>
            {
                var e = entra.Value;
                bearer.Authority = $"https://login.microsoftonline.com/{e.TenantId}/v2.0";
                bearer.Audience  = e.Audience;
            });

        services.AddScoped<IAuthorizationHandler, QuoteOwnerHandler>();

        // Outbound HTTP to Entra ID (OIDC metadata fetch).
        // Anything that calls another API gets the same resilience pipeline:
        // total timeout → retry → circuit breaker → per-attempt timeout.
        services
            .AddHttpClient<IEntraIdMetadataClient, EntraIdMetadataClient>("entra-id", (sp, client) =>
            {
                var entra = sp.GetRequiredService<IOptions<EntraIdOptions>>().Value;
                // Tenant may be empty in test/dev — fall back to "common" so DI still composes.
                var tenant = string.IsNullOrWhiteSpace(entra.TenantId) ? "common" : entra.TenantId;
                client.BaseAddress = new Uri($"https://login.microsoftonline.com/{tenant}/v2.0/");
            })
            .AddResilienceHandler("default", (pipeline, ctx) =>
            {
                var logger = ctx.ServiceProvider
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("HttpResilience.entra-id");
                pipeline.AddDefaultResiliencePipeline(logger, pipelineName: "entra-id");
            });

        var appInsightsConnection = configuration["AppInsights:ConnectionString"];
        var useAzureMonitor = !string.IsNullOrWhiteSpace(appInsightsConnection);

        var otel = services
            .AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(QuotesTelemetry.ServiceName));

        if (useAzureMonitor)
        {
            otel.UseAzureMonitor(o => o.ConnectionString = appInsightsConnection);
            otel.WithTracing(t => t
                .AddSource(QuotesTelemetry.ServiceName)
                .AddEntityFrameworkCoreInstrumentation(o => o.SetDbStatementForText = true));
        }
        else
        {
            otel.WithTracing(t => t
                .AddSource(QuotesTelemetry.ServiceName)
                .AddAspNetCoreInstrumentation()
                .AddEntityFrameworkCoreInstrumentation(o => o.SetDbStatementForText = true)
                .AddHttpClientInstrumentation()
                .AddOtlpExporter());
        }

        services.AddAuthorization(options =>
        {
            // Policy 1: claim-based — token must carry scope=quotes.write
            options.AddPolicy("can-edit-quotes",
                p => p.RequireClaim("scope", "quotes.write"));

            // Policy 2: custom requirement — authenticated user must own the quote
            options.AddPolicy("can-delete-own-quote",
                p => p.Requirements.Add(new QuoteOwnerRequirement()));
        });

        return services;
    }
}
