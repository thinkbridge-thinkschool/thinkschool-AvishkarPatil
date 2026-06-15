namespace BusBooking.Application.Booking.Commands.CancelBooking;

public sealed record CancelBookingCommand(Guid BookingId, Guid RequestingUserId);
