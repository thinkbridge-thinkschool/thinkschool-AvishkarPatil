using BusBooking.Application.Booking.Repositories;
using BusBooking.Application.Common;
using BusBooking.Application.Common.Exceptions;
using BusBooking.Application.Scheduling.Repositories;
using BusBooking.Domain.Booking.Events;

namespace BusBooking.Application.Booking.Commands.CancelBooking;

public sealed class CancelBookingHandler(
    IBookingRepository bookingRepo,
    IScheduleRepository scheduleRepo,
    IEventPublisher publisher)
{
    public async Task HandleAsync(CancelBookingCommand command, CancellationToken ct = default)
    {
        var booking = await bookingRepo.GetByIdAsync(command.BookingId, ct)
            ?? throw new NotFoundException("Booking", command.BookingId);

        if (booking.UserId != command.RequestingUserId)
            throw new UnauthorizedAccessException("You can only cancel your own bookings.");

        // Load schedule and release seats synchronously within the same transaction.
        // In a modular monolith both contexts share the DB — no need to go through Service Bus
        // for seat state. Service Bus is still used for Notifications (email on cancellation).
        var schedule = await scheduleRepo.GetByIdWithSeatsAsync(booking.ScheduleId, ct)
            ?? throw new NotFoundException("Schedule", booking.ScheduleId);

        var seatNumbers = booking.Seats.Select(s => s.SeatNumber).ToList();
        schedule.ReleaseSeats(seatNumbers);

        booking.Cancel();

        await bookingRepo.SaveChangesAsync(ct);

        // Publish BookingCancelledEvent → Notifications context sends cancellation email
        foreach (var evt in booking.DomainEvents)
            await publisher.PublishAsync(evt, ct);
        booking.ClearDomainEvents();
    }
}
