using BusBooking.Domain.Scheduling.Entities;
using BusBooking.Domain.Scheduling.Enums;

namespace BusBooking.Domain.Tests.Scheduling;

// These tests verify the DOMAIN invariants that underpin double-booking prevention.
// The EF RowVersion enforcement (what catches two concurrent SaveChangesAsync calls)
// belongs in an integration test project that can hit a real database.
// See DESIGN.md §Concurrency for the full collide-and-retry flow.
public sealed class SeatConcurrencyTests
{
    private static Seat MakeAvailableSeat(int number = 1) =>
        Seat.Create(Guid.NewGuid(), number, SeatType.Window, 400m);

    // ─── Seat-level invariants ────────────────────────────────────────────────

    [Fact]
    public void Reserve_WhenAvailable_SetsReservedAndLocksTimestamp()
    {
        var seat = MakeAvailableSeat();
        var before = DateTime.UtcNow;

        seat.Reserve();

        Assert.Equal(SeatStatus.Reserved, seat.Status);
        Assert.NotNull(seat.LockedAt);
        Assert.True(seat.LockedAt >= before);
    }

    [Fact]
    public void Reserve_WhenAlreadyReserved_Throws()
    {
        // This is what the second concurrent request hits when it reads stale Available
        // state, calls Reserve() in memory, and EF's RowVersion rejects SaveChanges.
        // Even if RowVersion somehow didn't fire, calling Reserve() on a Reserved seat
        // throws before any DB write, giving a second line of defence.
        var seat = MakeAvailableSeat();
        seat.Reserve();

        var ex = Assert.Throws<InvalidOperationException>(() => seat.Reserve());
        Assert.Contains("not available", ex.Message);
    }

    [Fact]
    public void Reserve_WhenBooked_Throws()
    {
        var seat = MakeAvailableSeat();
        seat.Reserve();
        seat.Book();

        var ex = Assert.Throws<InvalidOperationException>(() => seat.Reserve());
        Assert.Contains("not available", ex.Message);
    }

    [Fact]
    public void Book_WhenNotReserved_Throws()
    {
        var seat = MakeAvailableSeat();

        Assert.Throws<InvalidOperationException>(() => seat.Book());
    }

    [Fact]
    public void IsLockExpired_ReturnsFalse_WhenJustReserved()
    {
        var seat = MakeAvailableSeat();
        seat.Reserve();

        Assert.False(seat.IsLockExpired());
    }

    [Fact]
    public void Release_ResetsStatusAndClearsLock()
    {
        var seat = MakeAvailableSeat();
        seat.Reserve();
        seat.Release();

        Assert.Equal(SeatStatus.Available, seat.Status);
        Assert.Null(seat.LockedAt);
        Assert.False(seat.IsLockExpired());
    }

    // ─── Schedule-level double-booking prevention ─────────────────────────────

    [Fact]
    public void ReserveSeats_WhenSeatAlreadyReserved_Throws()
    {
        // Simulates: Request A reserved seat 5. Request B (with stale in-memory state)
        // also tries to reserve seat 5. Domain throws before hitting the DB at all.
        var scheduleId = Guid.NewGuid();
        var seat = Seat.Create(scheduleId, 5, SeatType.Window, 400m);
        var schedule = Schedule.Create(Guid.NewGuid(), Guid.NewGuid(),
            DateOnly.FromDateTime(DateTime.UtcNow), new TimeOnly(8, 0), new TimeOnly(12, 0));
        schedule.AddSeats([seat]);

        schedule.ReserveSeats([5]); // Request A succeeds

        // Request B: same schedule object in memory — seat is now Reserved.
        // In a real concurrent scenario, Request B would have its own DB context and
        // would read the seat as Available; the RowVersion on SaveChanges would then
        // reject it. Here we prove the domain invariant directly.
        var ex = Assert.Throws<InvalidOperationException>(() => schedule.ReserveSeats([5]));
        Assert.Contains("not available", ex.Message);
    }

    [Fact]
    public void ReserveSeats_WhenSeatDoesNotExist_Throws()
    {
        var scheduleId = Guid.NewGuid();
        var seat = Seat.Create(scheduleId, 1, SeatType.Window, 400m);
        var schedule = Schedule.Create(Guid.NewGuid(), Guid.NewGuid(),
            DateOnly.FromDateTime(DateTime.UtcNow), new TimeOnly(8, 0), new TimeOnly(12, 0));
        schedule.AddSeats([seat]);

        var ex = Assert.Throws<InvalidOperationException>(() => schedule.ReserveSeats([99]));
        Assert.Contains("do not exist", ex.Message);
    }

    [Fact]
    public void ReserveSeats_WithDuplicateSeatNumbers_Throws()
    {
        // Passing [5, 5] should not succeed — only one seat found vs two requested.
        var scheduleId = Guid.NewGuid();
        var seat = Seat.Create(scheduleId, 5, SeatType.Aisle, 350m);
        var schedule = Schedule.Create(Guid.NewGuid(), Guid.NewGuid(),
            DateOnly.FromDateTime(DateTime.UtcNow), new TimeOnly(8, 0), new TimeOnly(12, 0));
        schedule.AddSeats([seat]);

        Assert.Throws<InvalidOperationException>(() => schedule.ReserveSeats([5, 5]));
    }

    [Fact]
    public void ReserveSeats_ThenBookSeats_TransitionsCorrectly()
    {
        var scheduleId = Guid.NewGuid();
        var seat3 = Seat.Create(scheduleId, 3, SeatType.Window, 400m);
        var seat7 = Seat.Create(scheduleId, 7, SeatType.Aisle, 350m);
        var schedule = Schedule.Create(Guid.NewGuid(), Guid.NewGuid(),
            DateOnly.FromDateTime(DateTime.UtcNow), new TimeOnly(8, 0), new TimeOnly(12, 0));
        schedule.AddSeats([seat3, seat7]);

        var prices = schedule.ReserveSeats([3, 7]);
        Assert.Equal(400m, prices[3]);
        Assert.Equal(350m, prices[7]);
        Assert.Equal(SeatStatus.Reserved, seat3.Status);
        Assert.Equal(SeatStatus.Reserved, seat7.Status);

        schedule.BookSeats([3, 7]);
        Assert.Equal(SeatStatus.Booked, seat3.Status);
        Assert.Equal(SeatStatus.Booked, seat7.Status);
        Assert.Equal(0, schedule.AvailableSeatsCount);
    }

    [Fact]
    public void ReleaseSeats_AfterBooking_ResetsToAvailable()
    {
        var scheduleId = Guid.NewGuid();
        var seat = Seat.Create(scheduleId, 1, SeatType.Middle, 375m);
        var schedule = Schedule.Create(Guid.NewGuid(), Guid.NewGuid(),
            DateOnly.FromDateTime(DateTime.UtcNow), new TimeOnly(8, 0), new TimeOnly(12, 0));
        schedule.AddSeats([seat]);

        schedule.ReserveSeats([1]);
        schedule.BookSeats([1]);
        schedule.ReleaseSeats([1]);

        Assert.Equal(SeatStatus.Available, seat.Status);
        Assert.Equal(1, schedule.AvailableSeatsCount);
    }
}
