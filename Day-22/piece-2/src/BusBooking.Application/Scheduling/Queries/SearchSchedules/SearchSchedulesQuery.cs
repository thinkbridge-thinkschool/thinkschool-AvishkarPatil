namespace BusBooking.Application.Scheduling.Queries.SearchSchedules;

public sealed record SearchSchedulesQuery(string Source, string Destination, DateOnly TravelDate);
