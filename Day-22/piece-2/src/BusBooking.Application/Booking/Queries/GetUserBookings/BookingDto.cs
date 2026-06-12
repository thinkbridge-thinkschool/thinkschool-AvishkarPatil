using BusBooking.Domain.Booking.Enums;

namespace BusBooking.Application.Booking.Queries.GetUserBookings;

public sealed record BookingDto(
    Guid BookingId,
    Guid ScheduleId,
    BookingStatus Status,
    decimal TotalAmount,
    DateTime BookedAt,
    IReadOnlyList<BookedSeatDto> Seats);

public sealed record BookedSeatDto(
    int SeatNumber,
    string PassengerName,
    int PassengerAge,
    decimal SeatPrice);
