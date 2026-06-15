using BusBooking.Application.Booking.Commands.CancelBooking;
using BusBooking.Application.Booking.Commands.CreateBooking;
using BusBooking.Application.Booking.Queries.GetUserBookings;
using BusBooking.Application.Booking.Repositories;
using BusBooking.Application.Common;
using BusBooking.Application.Common.Exceptions;
using BusBooking.Application.Scheduling.Repositories;

namespace BusBooking.Api.Booking;

public static class BookingEndpoints
{
    public static void MapBookingEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/bookings").WithTags("Bookings");

        group.MapPost("/", CreateBooking);
        group.MapGet("/user/{userId:guid}", GetUserBookings);
        group.MapPost("/{bookingId:guid}/cancel", CancelBooking);
    }

    private static async Task<IResult> CreateBooking(
        CreateBookingCommand command,
        IScheduleRepository scheduleRepo,
        IBookingRepository bookingRepo,
        IEventPublisher publisher,
        CancellationToken ct)
    {
        var handler = new CreateBookingHandler(scheduleRepo, bookingRepo, publisher);
        try
        {
            var bookingId = await handler.HandleAsync(command, ct);
            return Results.Created($"/api/bookings/{bookingId}", new { bookingId });
        }
        catch (NotFoundException ex) { return Results.NotFound(ex.Message); }
        catch (InvalidOperationException ex) { return Results.Conflict(ex.Message); }
    }

    private static async Task<IResult> GetUserBookings(
        Guid userId,
        IBookingRepository bookingRepo,
        CancellationToken ct)
    {
        var handler = new GetUserBookingsHandler(bookingRepo);
        var dtos = await handler.HandleAsync(new GetUserBookingsQuery(userId), ct);
        return Results.Ok(dtos);
    }

    private static async Task<IResult> CancelBooking(
        Guid bookingId,
        CancelBookingRequest req,
        IBookingRepository bookingRepo,
        IScheduleRepository scheduleRepo,
        IEventPublisher publisher,
        CancellationToken ct)
    {
        var handler = new CancelBookingHandler(bookingRepo, scheduleRepo, publisher);
        try
        {
            await handler.HandleAsync(new CancelBookingCommand(bookingId, req.RequestingUserId), ct);
            return Results.NoContent();
        }
        catch (NotFoundException ex) { return Results.NotFound(ex.Message); }
        catch (UnauthorizedAccessException) { return Results.Forbid(); }
        catch (InvalidOperationException ex) { return Results.Conflict(ex.Message); }
    }
}

public sealed record CancelBookingRequest(Guid RequestingUserId);
