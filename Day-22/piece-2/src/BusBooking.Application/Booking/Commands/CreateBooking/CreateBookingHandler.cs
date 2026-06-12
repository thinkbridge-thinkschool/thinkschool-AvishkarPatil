using BusBooking.Application.Booking.Repositories;
using BusBooking.Application.Common;
using BusBooking.Application.Common.Exceptions;
using BusBooking.Application.Scheduling.Repositories;
using BusBooking.Domain.Booking.ValueObjects;
using BookingAggregate = BusBooking.Domain.Booking.Aggregates.Booking;

namespace BusBooking.Application.Booking.Commands.CreateBooking;

public sealed class CreateBookingHandler(
    IScheduleRepository scheduleRepo,
    IBookingRepository bookingRepo,
    IEventPublisher publisher)
{
    public async Task<Guid> HandleAsync(CreateBookingCommand command, CancellationToken ct = default)
    {
        // 1. Load schedule with seat inventory
        var schedule = await scheduleRepo.GetByIdWithSeatsAsync(command.ScheduleId, ct)
            ?? throw new NotFoundException("Schedule", command.ScheduleId);

        // 2. Reserve seats atomically — throws if any seat is unavailable or doesn't exist
        var seatNumbers = command.Seats.Select(s => s.SeatNumber).ToList();
        var priceMap = schedule.ReserveSeats(seatNumbers);

        // 3. Map to BookedSeat value objects capturing the price snapshot
        var bookedSeats = command.Seats.Select(s => new BookedSeat(
            s.SeatNumber,
            s.PassengerName,
            s.PassengerAge,
            s.PassengerGender,
            priceMap[s.SeatNumber]));

        // 4. Create booking aggregate and confirm (payment stubbed as always-success)
        var booking = BookingAggregate.Create(command.UserId, command.UserEmail, command.ScheduleId, bookedSeats);
        booking.Confirm(command.UserName);

        // 5. Transition seats from Reserved → Booked now that payment is confirmed
        schedule.BookSeats(seatNumbers);

        // 6. Persist both schedule (seat status changed) and new booking
        await bookingRepo.AddAsync(booking, ct);
        await bookingRepo.SaveChangesAsync(ct);

        // 7. Dispatch domain events to Service Bus
        foreach (var evt in booking.DomainEvents)
            await publisher.PublishAsync(evt, ct);
        booking.ClearDomainEvents();

        return booking.Id;
    }
}
