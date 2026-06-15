using BusBooking.Domain.Common;

namespace BusBooking.Domain.Booking.Events;

public sealed record BookingConfirmedEvent(
    Guid BookingId,
    string UserEmail,
    string UserName,
    Guid ScheduleId,
    decimal TotalAmount,
    IReadOnlyList<int> SeatNumbers) : IDomainEvent;
