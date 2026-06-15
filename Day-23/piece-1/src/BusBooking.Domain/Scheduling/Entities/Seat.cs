using BusBooking.Domain.Common;
using BusBooking.Domain.Scheduling.Enums;

namespace BusBooking.Domain.Scheduling.Entities;

public sealed class Seat : BaseEntity
{
    private static readonly TimeSpan LockTimeout = TimeSpan.FromMinutes(10);

    public Guid ScheduleId { get; private set; }
    public int SeatNumber { get; private set; }
    public SeatType SeatType { get; private set; }
    public decimal Price { get; private set; }
    public SeatStatus Status { get; private set; } = SeatStatus.Available;
    public DateTime? LockedAt { get; private set; }

    private Seat() { }

    public static Seat Create(Guid scheduleId, int seatNumber, SeatType seatType, decimal price) =>
        new() { ScheduleId = scheduleId, SeatNumber = seatNumber, SeatType = seatType, Price = price };

    public void Reserve()
    {
        if (Status != SeatStatus.Available)
            throw new InvalidOperationException($"Seat {SeatNumber} is not available for reservation.");
        Status = SeatStatus.Reserved;
        LockedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Book()
    {
        if (Status != SeatStatus.Reserved)
            throw new InvalidOperationException($"Seat {SeatNumber} must be reserved before booking.");
        Status = SeatStatus.Booked;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Release()
    {
        Status = SeatStatus.Available;
        LockedAt = null;
        UpdatedAt = DateTime.UtcNow;
    }

    public bool IsLockExpired() =>
        Status == SeatStatus.Reserved &&
        LockedAt.HasValue &&
        DateTime.UtcNow - LockedAt.Value > LockTimeout;
}
