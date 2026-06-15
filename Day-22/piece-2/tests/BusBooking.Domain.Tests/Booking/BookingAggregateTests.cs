using BusBooking.Domain.Booking.Enums;
using BusBooking.Domain.Booking.Events;
using BusBooking.Domain.Booking.ValueObjects;
using BookingAggregate = BusBooking.Domain.Booking.Aggregates.Booking;

namespace BusBooking.Domain.Tests.Booking;

public sealed class BookingAggregateTests
{
    private static BookingAggregate MakeBooking() => BookingAggregate.Create(
        userId: Guid.NewGuid(),
        userEmail: "user@example.com",
        scheduleId: Guid.NewGuid(),
        seats: [new BookedSeat(1, "Avishkar", 25, "Male", 450m)]);

    [Fact]
    public void Create_ShouldCalculateTotalAmount()
    {
        var seats = new[]
        {
            new BookedSeat(1, "Alice", 28, "Female", 400m),
            new BookedSeat(2, "Bob", 30, "Male", 500m),
        };
        var booking = BookingAggregate.Create(Guid.NewGuid(), "alice@x.com", Guid.NewGuid(), seats);

        Assert.Equal(900m, booking.TotalAmount);
        Assert.Equal(BookingStatus.Pending, booking.Status);
        Assert.Empty(booking.DomainEvents);
    }

    [Fact]
    public void Confirm_ShouldTransitionToConfirmed_AndRaiseEvent()
    {
        var booking = MakeBooking();
        booking.Confirm("Avishkar Patil");

        Assert.Equal(BookingStatus.Confirmed, booking.Status);
        var evt = Assert.Single(booking.DomainEvents);
        Assert.IsType<BookingConfirmedEvent>(evt);

        var confirmed = (BookingConfirmedEvent)evt;
        Assert.Equal(booking.Id, confirmed.BookingId);
        Assert.Equal("user@example.com", confirmed.UserEmail);
    }

    [Fact]
    public void Cancel_ShouldTransitionToCancelled_AndRaiseCancelledEvent()
    {
        var booking = MakeBooking();
        booking.Confirm("Avishkar");
        booking.ClearDomainEvents();

        booking.Cancel();

        Assert.Equal(BookingStatus.Cancelled, booking.Status);
        var evt = Assert.Single(booking.DomainEvents);
        Assert.IsType<BookingCancelledEvent>(evt);

        var cancelled = (BookingCancelledEvent)evt;
        Assert.Contains(1, cancelled.ReleasedSeatNumbers);
    }

    [Fact]
    public void Cancel_WhenAlreadyCancelled_ShouldThrow()
    {
        var booking = MakeBooking();
        booking.Cancel();

        Assert.Throws<InvalidOperationException>(() => booking.Cancel());
    }

    [Fact]
    public void Create_WithNoSeats_ShouldThrow()
    {
        Assert.Throws<InvalidOperationException>(() =>
            BookingAggregate.Create(Guid.NewGuid(), "x@x.com", Guid.NewGuid(), []));
    }
}
