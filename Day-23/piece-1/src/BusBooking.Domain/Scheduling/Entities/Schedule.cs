using BusBooking.Domain.Common;
using BusBooking.Domain.Scheduling.Enums;

namespace BusBooking.Domain.Scheduling.Entities;

public sealed class Schedule : BaseEntity
{
    public Guid BusId { get; private set; }
    public Guid RouteId { get; private set; }
    public DateOnly TravelDate { get; private set; }
    public TimeOnly DepartureTime { get; private set; }
    public TimeOnly ArrivalTime { get; private set; }
    public bool IsActive { get; private set; } = true;

    private readonly List<Seat> _seats = [];
    public IReadOnlyCollection<Seat> Seats => _seats.AsReadOnly();
    public int AvailableSeatsCount => _seats.Count(s => s.Status == SeatStatus.Available);

    private Schedule() { }

    public static Schedule Create(Guid busId, Guid routeId, DateOnly travelDate, TimeOnly departure, TimeOnly arrival) =>
        new() { BusId = busId, RouteId = routeId, TravelDate = travelDate, DepartureTime = departure, ArrivalTime = arrival };

    public void AddSeats(IEnumerable<Seat> seats) => _seats.AddRange(seats);

    // Returns price per seat for the reserved seats; throws if any seat is unavailable.
    public Dictionary<int, decimal> ReserveSeats(IReadOnlyList<int> seatNumbers)
    {
        var seats = _seats.Where(s => seatNumbers.Contains(s.SeatNumber)).ToList();

        if (seats.Count != seatNumbers.Count)
            throw new InvalidOperationException("One or more requested seat numbers do not exist on this schedule.");

        foreach (var seat in seats)
            seat.Reserve(); // throws if not Available

        UpdatedAt = DateTime.UtcNow;
        return seats.ToDictionary(s => s.SeatNumber, s => s.Price);
    }

    public void BookSeats(IReadOnlyList<int> seatNumbers)
    {
        foreach (var seat in _seats.Where(s => seatNumbers.Contains(s.SeatNumber)))
            seat.Book();
        UpdatedAt = DateTime.UtcNow;
    }

    public void ReleaseSeats(IReadOnlyList<int> seatNumbers)
    {
        foreach (var seat in _seats.Where(s => seatNumbers.Contains(s.SeatNumber)))
            seat.Release();
        UpdatedAt = DateTime.UtcNow;
    }

    public IReadOnlyList<Seat> GetExpiredReservations() =>
        _seats.Where(s => s.IsLockExpired()).ToList();

    public void Deactivate()
    {
        IsActive = false;
        UpdatedAt = DateTime.UtcNow;
    }
}
