using BusBooking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BusBooking.Infrastructure.BackgroundServices;

// Replaces Spring Boot's @Scheduled — runs every 5 minutes to release expired seat reservations.
internal sealed class SeatExpiryService(
    IServiceScopeFactory scopeFactory,
    ILogger<SeatExpiryService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(Interval, stoppingToken);
            try
            {
                await ReleaseExpiredReservationsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown — let the loop exit.
                break;
            }
            catch (Exception ex)
            {
                // Log and continue — an unhandled exception here would kill the loop permanently,
                // meaning no expired reservations would ever be released until process restart.
                // DbUpdateConcurrencyException is the common case: a booking won the race on a
                // seat we were about to release; the next poll cycle will see the correct state.
                logger.LogWarning(ex,
                    "SeatExpiryService: failed to release expired reservations; will retry in {Interval}",
                    Interval);
            }
        }
    }

    private async Task ReleaseExpiredReservationsAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BusBookingDbContext>();

        var schedules = await db.Schedules
            .Where(s => s.IsActive)
            .ToListAsync(ct);

        int released = 0;
        foreach (var schedule in schedules)
        {
            var expired = schedule.GetExpiredReservations();
            foreach (var seat in expired)
            {
                seat.Release();
                released++;
            }
        }

        if (released > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("SeatExpiryService: released {Count} expired seat reservations", released);
        }
    }
}
