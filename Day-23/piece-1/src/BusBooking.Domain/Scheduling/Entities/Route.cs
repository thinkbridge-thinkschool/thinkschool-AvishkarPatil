using BusBooking.Domain.Common;

namespace BusBooking.Domain.Scheduling.Entities;

public sealed class Route : BaseEntity
{
    public string Source { get; private set; } = default!;
    public string Destination { get; private set; } = default!;
    public string Name => $"{Source} → {Destination}";

    private Route() { }

    public static Route Create(string source, string destination)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        return new Route { Source = source, Destination = destination };
    }
}
