using BusBooking.Domain.Scheduling.Entities;
using BusBooking.Domain.Scheduling.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BusBooking.Infrastructure.Persistence;

public sealed class DatabaseSeeder(BusBookingDbContext db, ILogger<DatabaseSeeder> logger)
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        var today    = DateOnly.FromDateTime(DateTime.UtcNow);
        var tomorrow = today.AddDays(1);

        if (await db.Routes.AnyAsync(ct))
        {
            // Routes/buses already exist — only top-up schedules for today/tomorrow.
            await EnsureSchedulesAsync(today, tomorrow, ct);
            return;
        }

        logger.LogInformation("Seeding database...");

        // Routes
        var puneMumbai  = Route.Create("Pune", "Mumbai");
        var mumbaiPune  = Route.Create("Mumbai", "Pune");
        var puneNagpur  = Route.Create("Pune", "Nagpur");
        var nagpurPune  = Route.Create("Nagpur", "Pune");

        await db.Routes.AddRangeAsync([puneMumbai, mumbaiPune, puneNagpur, nagpurPune], ct);

        // Buses
        var vendorId = Guid.NewGuid();
        var bus1 = Bus.Create("MH12-AB-1234", "Shivneri Express", BusType.Seater,    40, vendorId);
        var bus2 = Bus.Create("MH12-CD-5678", "Volvo Sleeper",    BusType.Sleeper,   36, vendorId);
        var bus3 = Bus.Create("MH12-EF-9012", "City Link Semi",   BusType.SemiSleeper, 38, vendorId);

        await db.Buses.AddRangeAsync([bus1, bus2, bus3], ct);
        await db.SaveChangesAsync(ct);

        await EnsureSchedulesAsync(today, tomorrow, ct);

        logger.LogInformation("Seeded 4 routes, 3 buses.");
    }

    private async Task EnsureSchedulesAsync(DateOnly today, DateOnly tomorrow, CancellationToken ct)
    {
        var dates = new[] { today, tomorrow };

        // Load reference data
        var routes = await db.Routes.ToListAsync(ct);
        var buses  = await db.Buses.ToListAsync(ct);

        var puneMumbai = routes.First(r => r.Source == "Pune"    && r.Destination == "Mumbai");
        var mumbaiPune = routes.First(r => r.Source == "Mumbai"  && r.Destination == "Pune");
        var puneNagpur = routes.First(r => r.Source == "Pune"    && r.Destination == "Nagpur");
        var nagpurPune = routes.First(r => r.Source == "Nagpur"  && r.Destination == "Pune");

        var bus1 = buses.First(b => b.BusNumber == "MH12-AB-1234");
        var bus2 = buses.First(b => b.BusNumber == "MH12-CD-5678");
        var bus3 = buses.First(b => b.BusNumber == "MH12-EF-9012");

        // Existing schedule (routeId, busId, date) combos — avoids duplicates on re-runs
        var existing = await db.Schedules
            .Where(s => dates.Contains(s.TravelDate))
            .Select(s => new { s.RouteId, s.BusId, s.TravelDate })
            .ToListAsync(ct);

        bool Exists(Guid routeId, Guid busId, DateOnly date) =>
            existing.Any(e => e.RouteId == routeId && e.BusId == busId && e.TravelDate == date);

        var toAdd = new List<Domain.Scheduling.Entities.Schedule>();

        void TryAdd(Guid busId, Guid routeId, DateOnly date, TimeOnly dep, TimeOnly arr,
                    int seats, decimal win, decimal aisle)
        {
            if (!Exists(routeId, busId, date))
                toAdd.Add(MakeSchedule(busId, routeId, date, dep, arr, seats, win, aisle));
        }

        // Pune → Mumbai
        TryAdd(bus1.Id, puneMumbai.Id, today,    new TimeOnly(6,  0), new TimeOnly(9,  30), 40, 350m, 400m);
        TryAdd(bus2.Id, puneMumbai.Id, today,    new TimeOnly(22, 0), new TimeOnly(1,  30), 36, 550m, 650m);
        TryAdd(bus1.Id, puneMumbai.Id, tomorrow, new TimeOnly(6,  0), new TimeOnly(9,  30), 40, 350m, 400m);

        // Mumbai → Pune
        TryAdd(bus3.Id, mumbaiPune.Id, today,    new TimeOnly(8,  0), new TimeOnly(11, 30), 38, 375m, 425m);
        TryAdd(bus2.Id, mumbaiPune.Id, tomorrow, new TimeOnly(22, 0), new TimeOnly(1,  30), 36, 550m, 650m);

        // Pune → Nagpur
        TryAdd(bus3.Id, puneNagpur.Id, today,    new TimeOnly(18, 0), new TimeOnly(6,  0),  38, 700m, 800m);
        TryAdd(bus2.Id, puneNagpur.Id, tomorrow, new TimeOnly(19, 0), new TimeOnly(7,  0),  36, 750m, 850m);

        // Nagpur → Pune
        TryAdd(bus1.Id, nagpurPune.Id, tomorrow, new TimeOnly(7,  0), new TimeOnly(19, 0),  40, 700m, 800m);

        if (toAdd.Count > 0)
        {
            await db.Schedules.AddRangeAsync(toAdd, ct);
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Added {Count} missing schedules for {Today} / {Tomorrow}.", toAdd.Count, today, tomorrow);
        }
        else
        {
            logger.LogInformation("Schedules for {Today} / {Tomorrow} already present.", today, tomorrow);
        }
    }

    private static Domain.Scheduling.Entities.Schedule MakeSchedule(
        Guid busId, Guid routeId,
        DateOnly date, TimeOnly departure, TimeOnly arrival,
        int totalSeats, decimal windowPrice, decimal aislePrice)
    {
        var schedule = Domain.Scheduling.Entities.Schedule.Create(busId, routeId, date, departure, arrival);

        var seats = Enumerable.Range(1, totalSeats).Select(n =>
        {
            var seatType = n % 3 == 0 ? SeatType.Aisle
                         : n % 3 == 1 ? SeatType.Window
                         : SeatType.Middle;

            var price = seatType == SeatType.Window ? windowPrice
                      : seatType == SeatType.Aisle  ? aislePrice
                      : (windowPrice + aislePrice) / 2;

            return Seat.Create(schedule.Id, n, seatType, price);
        });

        schedule.AddSeats(seats);
        return schedule;
    }
}
