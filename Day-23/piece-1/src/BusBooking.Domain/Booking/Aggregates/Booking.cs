using BusBooking.Domain.Booking.Enums;
using BusBooking.Domain.Booking.Events;
using BusBooking.Domain.Booking.ValueObjects;
using BusBooking.Domain.Common;

namespace BusBooking.Domain.Booking.Aggregates;

public sealed class Booking : BaseEntity
{
    public Guid UserId { get; private set; }
    public string UserEmail { get; private set; } = default!;
    public Guid ScheduleId { get; private set; }
    public BookingStatus Status { get; private set; } = BookingStatus.Pending;
    public decimal TotalAmount { get; private set; }
    public DateTime BookedAt { get; private set; }

    private readonly List<BookedSeat> _seats = [];
    public IReadOnlyCollection<BookedSeat> Seats => _seats.AsReadOnly();

    private Booking() { }

    public static Booking Create(Guid userId, string userEmail, Guid scheduleId, IEnumerable<BookedSeat> seats)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userEmail);

        var booking = new Booking
        {
            UserId = userId,
            UserEmail = userEmail,
            ScheduleId = scheduleId,
            BookedAt = DateTime.UtcNow,
        };

        booking._seats.AddRange(seats);

        if (booking._seats.Count == 0)
            throw new InvalidOperationException("A booking must have at least one seat.");

        booking.TotalAmount = booking._seats.Sum(s => s.SeatPrice);
        return booking;
    }

    public void Confirm(string userName)
    {
        if (Status != BookingStatus.Pending)
            throw new InvalidOperationException($"Cannot confirm a booking in '{Status}' status.");

        Status = BookingStatus.Confirmed;
        UpdatedAt = DateTime.UtcNow;

        RaiseDomainEvent(new BookingConfirmedEvent(
            Id, UserEmail, userName, ScheduleId, TotalAmount,
            _seats.Select(s => s.SeatNumber).ToList()));
    }

    public void Cancel()
    {
        if (Status is BookingStatus.Cancelled or BookingStatus.Completed)
            throw new InvalidOperationException($"Cannot cancel a booking in '{Status}' status.");

        Status = BookingStatus.Cancelled;
        UpdatedAt = DateTime.UtcNow;

        RaiseDomainEvent(new BookingCancelledEvent(
            Id, ScheduleId, _seats.Select(s => s.SeatNumber).ToList()));
    }

    public void Complete()
    {
        if (Status != BookingStatus.Confirmed)
            throw new InvalidOperationException("Only confirmed bookings can be completed.");

        Status = BookingStatus.Completed;
        UpdatedAt = DateTime.UtcNow;
    }
}
