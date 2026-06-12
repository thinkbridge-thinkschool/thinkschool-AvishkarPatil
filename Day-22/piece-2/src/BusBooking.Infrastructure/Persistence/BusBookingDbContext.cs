using BusBooking.Domain.Booking.Aggregates;
using BusBooking.Domain.Scheduling.Entities;
using Microsoft.EntityFrameworkCore;

namespace BusBooking.Infrastructure.Persistence;

public sealed class BusBookingDbContext(DbContextOptions<BusBookingDbContext> options) : DbContext(options)
{
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<Domain.Scheduling.Entities.Schedule> Schedules => Set<Domain.Scheduling.Entities.Schedule>();
    public DbSet<Seat> Seats => Set<Seat>();
    public DbSet<Bus> Buses => Set<Bus>();
    public DbSet<Route> Routes => Set<Route>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BusBookingDbContext).Assembly);
    }
}
