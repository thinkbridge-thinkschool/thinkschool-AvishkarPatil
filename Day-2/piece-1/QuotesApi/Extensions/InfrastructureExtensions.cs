using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Repositories;
using QuotesApi.Services;

namespace QuotesApi.Extensions;

public static class InfrastructureExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ── Scoped (one per request) ────────────────────────────────
        // DbContext is scoped by default via AddDbContext.
        // Repositories depend on DbContext, so they must also be scoped.
        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseSqlite(
                configuration.GetConnectionString("Default"));
        });

        services.AddScoped<IQuoteRepository, QuoteRepository>();
        services.AddScoped<ICollectionRepository, CollectionRepository>();

        // ── Singleton (one for the app's life) ──────────────────────
        // IClock is stateless and thread-safe — perfect singleton.
        // Tests can swap with a fake clock that returns a fixed time.
        services.AddSingleton<IClock, SystemClock>();

        // ── Transient (new instance every time) ─────────────────────
        // Each injection gets a fresh Guid.
        // Good for stateless, disposable services.
        services.AddTransient<IRequestIdProvider, RequestIdProvider>();

        return services;
    }
}