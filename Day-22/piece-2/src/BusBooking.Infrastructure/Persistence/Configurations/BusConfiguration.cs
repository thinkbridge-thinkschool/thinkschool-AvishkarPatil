using BusBooking.Domain.Scheduling.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BusBooking.Infrastructure.Persistence.Configurations;

internal sealed class BusConfiguration : IEntityTypeConfiguration<Bus>
{
    public void Configure(EntityTypeBuilder<Bus> builder)
    {
        builder.HasKey(b => b.Id);
        builder.Property(b => b.BusNumber).HasMaxLength(20).IsRequired();
        builder.Property(b => b.BusName).HasMaxLength(100).IsRequired();
        // Consistent with SeatType/SeatStatus: store enum as string for readability.
        builder.Property(b => b.BusType).HasConversion<string>().HasMaxLength(20);
        builder.Ignore(b => b.DomainEvents);
    }
}
