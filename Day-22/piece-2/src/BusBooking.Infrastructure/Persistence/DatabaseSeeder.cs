using BusBooking.Domain.Scheduling.Entities;
using BusBooking.Domain.Scheduling.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BusBooking.Infrastructure.Persistence;

public sealed class DatabaseSeeder(BusBookingDbContext db, ILogger<DatabaseSeeder> logger)
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (await db.Routes.AnyAsync(ct))
        {
            logger.LogInformation("Database already seeded — skipping.");
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

        // Schedules — today + tomorrow
        var today    = DateOnly.FromDateTime(DateTime.UtcNow);
        var tomorrow = today.AddDays(1);

        var schedules = new List<Domain.Scheduling.Entities.Schedule>
        {
            // Pune → Mumbai
            MakeSchedule(bus1.Id, puneMumbai.Id, today,    new TimeOnly(6,  0), new TimeOnly(9,  30), 40, 350m, 400m),
            MakeSchedule(bus2.Id, puneMumbai.Id, today,    new TimeOnly(22, 0), new TimeOnly(1,  30), 36, 550m, 650m),
            MakeSchedule(bus1.Id, puneMumbai.Id, tomorrow, new TimeOnly(6,  0), new TimeOnly(9,  30), 40, 350m, 400m),

            // Mumbai → Pune
            MakeSchedule(bus3.Id, mumbaiPune.Id,  today,    new TimeOnly(8,  0), new TimeOnly(11, 30), 38, 375m, 425m),
            MakeSchedule(bus2.Id, mumbaiPune.Id,  tomorrow, new TimeOnly(22, 0), new TimeOnly(1,  30), 36, 550m, 650m),

            // Pune → Nagpur
            MakeSchedule(bus3.Id, puneNagpur.Id, today,    new TimeOnly(18, 0), new TimeOnly(6,  0),  38, 700m, 800m),
            MakeSchedule(bus2.Id, puneNagpur.Id, tomorrow, new TimeOnly(19, 0), new TimeOnly(7,  0),  36, 750m, 850m),

            // Nagpur → Pune
            MakeSchedule(bus1.Id, nagpurPune.Id, tomorrow, new TimeOnly(7,  0), new TimeOnly(19, 0),  40, 700m, 800m),
        };

        await db.Schedules.AddRangeAsync(schedules, ct);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Seeded {Routes} routes, {Buses} buses, {Schedules} schedules.",
            4, 3, schedules.Count);
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
