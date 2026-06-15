using BusBooking.Domain.Scheduling.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BusBooking.Infrastructure.Persistence.Configurations;

internal sealed class RouteConfiguration : IEntityTypeConfiguration<Route>
{
    public void Configure(EntityTypeBuilder<Route> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Source).HasMaxLength(100).IsRequired();
        builder.Property(r => r.Destination).HasMaxLength(100).IsRequired();
        // SearchAsync filters on (Source, Destination) on every search request.
        builder.HasIndex(r => new { r.Source, r.Destination });
        // Computed property — no column.
        builder.Ignore(r => r.Name);
        builder.Ignore(r => r.DomainEvents);
    }
}
