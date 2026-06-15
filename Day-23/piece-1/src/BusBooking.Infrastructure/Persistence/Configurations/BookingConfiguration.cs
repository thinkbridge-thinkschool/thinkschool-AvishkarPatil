using BusBooking.Domain.Booking.Aggregates;
using BusBooking.Domain.Booking.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BusBooking.Infrastructure.Persistence.Configurations;

internal sealed class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        builder.HasKey(b => b.Id);

        builder.Property(b => b.UserEmail).HasMaxLength(256).IsRequired();
        builder.Property(b => b.TotalAmount).HasColumnType("decimal(18,2)");
        builder.Property(b => b.Status).HasConversion<string>().HasMaxLength(20);

        // BookedSeat as owned JSON collection (EF 8+ owned-to-JSON)
        builder.OwnsMany(b => b.Seats, seats =>
        {
            seats.ToJson();
            seats.Property(s => s.PassengerName).HasMaxLength(100);
            seats.Property(s => s.PassengerGender).HasMaxLength(10);
            seats.Property(s => s.SeatPrice).HasColumnType("decimal(18,2)");
        });

        // Ignore domain events — not persisted
        builder.Ignore(b => b.DomainEvents);

        builder.HasIndex(b => b.UserId);
        builder.HasIndex(b => b.ScheduleId);
    }
}
