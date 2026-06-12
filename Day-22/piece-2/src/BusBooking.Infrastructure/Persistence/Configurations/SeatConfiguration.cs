using BusBooking.Domain.Scheduling.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BusBooking.Infrastructure.Persistence.Configurations;

internal sealed class SeatConfiguration : IEntityTypeConfiguration<Seat>
{
    public void Configure(EntityTypeBuilder<Seat> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.SeatType).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.Price).HasColumnType("decimal(18,2)");

        // Optimistic concurrency — prevents double-booking under concurrent writes
        builder.Property<byte[]>("RowVersion").IsRowVersion();

        builder.Ignore(s => s.DomainEvents);
        builder.HasIndex(s => new { s.ScheduleId, s.SeatNumber }).IsUnique();
    }
}
