namespace BusBooking.Domain.Booking.ValueObjects;

public sealed record BookedSeat(
    int SeatNumber,
    string PassengerName,
    int PassengerAge,
    string PassengerGender,
    decimal SeatPrice);
