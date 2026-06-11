using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Azure.Messaging.ServiceBus;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using QuotesApi.Application.Commands.Collections;
using QuotesApi.Application.Queries.Collections;
using QuotesApi.Authorization;
using QuotesApi.BackgroundJobs;
using QuotesApi.Configuration;
using QuotesApi.Data;
using QuotesApi.Messaging;
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
        // ── Day-21: HybridCache (L1 in-memory + L2 Redis) ────────────────────
        // Redis is optional — when the connection string is absent (e.g. local dev
        // without Docker) HybridCache silently operates as L1-only.  In production
        // every node shares the same Redis L2, so a cold-start on one instance
        // doesn't fan out to the database on every node.
        //
        // Stampede protection: HybridCache.GetOrCreateAsync coalesces concurrent
        // misses for the same key into a single factory invocation.  All waiters
        // share the same Task — only one DB round-trip fires regardless of how many
        // requests arrive simultaneously.
        var redisConnection = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redisConnection))
        {
            services.AddStackExchangeRedisCache(o =>
            {
                // abortConnect=false  — do not throw on startup if Redis is
                //   temporarily unreachable; StackExchange.Redis will reconnect
                //   in the background and HybridCache degrades to L1-only meanwhile.
                // connectTimeout=1000 — cap the initial connect handshake to 1 s
                //   so a dead Redis host fails fast instead of blocking for the
                //   default 5 s and stalling the first N cache misses.
                // syncTimeout=500     — cap per-operation blocking to 500 ms;
                //   after this HybridCache treats the L2 write/read as failed
                //   and falls back to L1 without propagating an exception.
                o.Configuration = redisConnection
                    + ",abortConnect=false,connectTimeout=1000,syncTimeout=500";
                o.InstanceName  = "QuotesApi:";
            });
        }

        services.AddHybridCache(o =>
        {
            o.DefaultEntryOptions = new HybridCacheEntryOptions
            {
                // L1 (in-process MemoryCache) TTL — keeps hot items off the Redis
                // wire entirely for the most recent 30 seconds.
                LocalCacheExpiration = TimeSpan.FromSeconds(30),
                // L2 (Redis) TTL — surviving across process restarts.
                Expiration = TimeSpan.FromMinutes(5),
            };
        });

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

        // ── Day-18: background jobs ───────────────────────────────────────
        // Singleton: the channel must outlive individual requests.
        services.AddSingleton<IQuoteAuditQueue, QuoteAuditQueue>();
        services.AddHostedService<QuoteAuditWorker>();

        // ── Day-19: Azure Service Bus topics ─────────────────────────────
        services
            .AddOptions<ServiceBusOptions>()
            .Bind(configuration.GetSection(ServiceBusOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // ServiceBusClient is thread-safe and should be a singleton.
        services.AddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<ServiceBusOptions>>().Value;
            return new ServiceBusClient(opts.ConnectionString);
        });

        // Publisher: singleton — holds a reusable ServiceBusSender.
        services.AddSingleton<IQuotePublisher, QuotePublisher>();

        // Subscription consumers as hosted services.
        services.AddHostedService<AnalyticsSubscriptionWorker>();
        services.AddHostedService<NotificationsSubscriptionWorker>();

        // Day-20: Outbox relay — polls OutboxMessages for unsent rows and publishes
        // them to Service Bus.  Must be registered AFTER IQuotePublisher (singleton)
        // because it injects IQuotePublisher directly.
        services.AddHostedService<OutboxRelayWorker>();

        services.AddScoped<IQuoteRepository, QuoteRepository>();
        services.AddScoped<ICollectionRepository, CollectionRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<ITokenService, TokenService>();

        // ── Day-12 piece-1 — CQRS-lite ───────────────────────────────────
        // CollectionQueryService is Singleton: it no longer captures a Scoped
        // AppDbContext directly — it uses IServiceScopeFactory to create a
        // child scope inside each factory lambda, so all its dependencies are
        // Singleton-safe (IServiceScopeFactory, HybridCache, ILogger).
        services.AddSingleton<ICollectionQueryService, CollectionQueryService>();
        services.AddScoped<CreateCollectionCommandHandler>();
        services.AddScoped<AddQuoteToCollectionCommandHandler>();

        // ── Day-12 piece-2 — Dapper hot-path implementation ──────────────
        // Registered alongside the EF version so both endpoints can be
        // exercised and timed under the same load test.
        // CollectionDapperQueryService opens its own SqlConnection — it does
        // not share the EF DbContext and does not participate in EF's
        // unit-of-work.  That is intentional: Dapper is for reads-only paths
        // where no write is expected in the same request.
        //
        // NOTE (Day-21): the Dapper route intentionally bypasses HybridCache.
        // Its purpose is raw latency benchmarking — adding a cache layer would
        // defeat the comparison.  As a consequence GET /api/collections/{id}/dapper
        // always returns fresh DB data while GET /api/collections/{id}/ef may
        // return a cached snapshot for up to 30 s after a write.  This is an
        // accepted trade-off for a diagnostic/benchmarking endpoint; do not
        // expose it in production without authentication.
        services.AddScoped<ICollectionDapperQueryService, CollectionDapperQueryService>();

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
