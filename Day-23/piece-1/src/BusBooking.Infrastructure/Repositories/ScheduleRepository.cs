using BusBooking.Application.Scheduling.Queries.SearchSchedules;
using BusBooking.Application.Scheduling.Repositories;
using BusBooking.Domain.Scheduling.Entities;
using BusBooking.Domain.Scheduling.Enums;
using BusBooking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BusBooking.Infrastructure.Repositories;

internal sealed class ScheduleRepository(BusBookingDbContext db) : IScheduleRepository
{
    public Task<Schedule?> GetByIdWithSeatsAsync(Guid scheduleId, CancellationToken ct = default) =>
        db.Schedules
          .Include(s => s.Seats)
          .FirstOrDefaultAsync(s => s.Id == scheduleId, ct);

    public async Task<IReadOnlyList<ScheduleSummaryDto>> SearchAsync(
        string source, string destination, DateOnly travelDate, CancellationToken ct = default)
    {
        return await db.Schedules
            .Where(s => s.TravelDate == travelDate && s.IsActive)
            .Include(s => s.Seats)
            .Join(db.Routes,
                  s => s.RouteId,
                  r => r.Id,
                  (s, r) => new { Schedule = s, Route = r })
            .Where(x => x.Route.Source == source && x.Route.Destination == destination)
            .Join(db.Buses,
                  x => x.Schedule.BusId,
                  b => b.Id,
                  (x, b) => new { x.Schedule, x.Route, Bus = b })
            .Select(x => new ScheduleSummaryDto(
                x.Schedule.Id,
                x.Bus.BusName,
                x.Bus.BusNumber,
                x.Route.Source,
                x.Route.Destination,
                x.Schedule.TravelDate,
                x.Schedule.DepartureTime,
                x.Schedule.ArrivalTime,
                x.Schedule.Seats.Count(seat => seat.Status == SeatStatus.Available),
                // Cast to decimal? so EF/LINQ returns null (not an exception) when no available seats exist.
                x.Schedule.Seats
                    .Where(seat => seat.Status == SeatStatus.Available)
                    .Select(seat => (decimal?)seat.Price)
                    .Min()))
            .ToListAsync(ct);
    }

    public async Task AddAsync(Schedule schedule, CancellationToken ct = default) =>
        await db.Schedules.AddAsync(schedule, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        db.SaveChangesAsync(ct);
}
