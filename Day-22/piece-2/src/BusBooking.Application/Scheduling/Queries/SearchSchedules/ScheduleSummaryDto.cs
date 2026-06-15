namespace BusBooking.Application.Scheduling.Queries.SearchSchedules;

public sealed record ScheduleSummaryDto(
    Guid ScheduleId,
    string BusName,
    string BusNumber,
    string Source,
    string Destination,
    DateOnly TravelDate,
    TimeOnly DepartureTime,
    TimeOnly ArrivalTime,
    int AvailableSeats,
    decimal? MinSeatPrice);  // null when the schedule is fully booked
