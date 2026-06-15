namespace BusBooking.Application.Booking.Commands.CreateBooking;

public sealed record CreateBookingCommand(
    Guid UserId,
    string UserEmail,
    string UserName,
    Guid ScheduleId,
    IReadOnlyList<SeatPassengerRequest> Seats);

public sealed record SeatPassengerRequest(
    int SeatNumber,
    string PassengerName,
    int PassengerAge,
    string PassengerGender);
