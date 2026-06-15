using BusBooking.Application.Scheduling.Repositories;

namespace BusBooking.Application.Scheduling.Queries.SearchSchedules;

public sealed class SearchSchedulesHandler(IScheduleRepository scheduleRepo)
{
    public Task<IReadOnlyList<ScheduleSummaryDto>> HandleAsync(
        SearchSchedulesQuery query, CancellationToken ct = default) =>
        scheduleRepo.SearchAsync(query.Source, query.Destination, query.TravelDate, ct);
}
