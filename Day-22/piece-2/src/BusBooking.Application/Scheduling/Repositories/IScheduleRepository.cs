using BusBooking.Application.Scheduling.Queries.SearchSchedules;
using BusBooking.Domain.Scheduling.Entities;

namespace BusBooking.Application.Scheduling.Repositories;

public interface IScheduleRepository
{
    Task<Schedule?> GetByIdWithSeatsAsync(Guid scheduleId, CancellationToken ct = default);
    Task<IReadOnlyList<ScheduleSummaryDto>> SearchAsync(string source, string destination, DateOnly travelDate, CancellationToken ct = default);
    Task AddAsync(Schedule schedule, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
