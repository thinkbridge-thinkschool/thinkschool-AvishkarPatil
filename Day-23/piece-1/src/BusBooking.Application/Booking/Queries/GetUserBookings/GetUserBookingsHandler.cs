using BusBooking.Application.Booking.Repositories;

namespace BusBooking.Application.Booking.Queries.GetUserBookings;

public sealed class GetUserBookingsHandler(IBookingRepository bookingRepo)
{
    public async Task<IReadOnlyList<BookingDto>> HandleAsync(
        GetUserBookingsQuery query, CancellationToken ct = default)
    {
        var bookings = await bookingRepo.GetByUserIdAsync(query.UserId, ct);

        return bookings
            .Select(b => new BookingDto(
                b.Id,
                b.ScheduleId,
                b.Status,
                b.TotalAmount,
                b.BookedAt,
                b.Seats
                    .Select(s => new BookedSeatDto(s.SeatNumber, s.PassengerName, s.PassengerAge, s.SeatPrice))
                    .ToList()))
            .ToList();
    }
}
