using BusBooking.Domain.Common;
using BusBooking.Domain.Scheduling.Enums;

namespace BusBooking.Domain.Scheduling.Entities;

public sealed class Bus : BaseEntity
{
    public string BusNumber { get; private set; } = default!;
    public string BusName { get; private set; } = default!;
    public BusType BusType { get; private set; }
    public int TotalSeats { get; private set; }
    public Guid VendorId { get; private set; }

    private Bus() { }

    public static Bus Create(string busNumber, string busName, BusType busType, int totalSeats, Guid vendorId) =>
        new() { BusNumber = busNumber, BusName = busName, BusType = busType, TotalSeats = totalSeats, VendorId = vendorId };
}
