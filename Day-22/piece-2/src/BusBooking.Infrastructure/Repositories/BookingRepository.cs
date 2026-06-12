using BusBooking.Application.Booking.Repositories;
using BusBooking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using BookingAggregate = BusBooking.Domain.Booking.Aggregates.Booking;

namespace BusBooking.Infrastructure.Repositories;

internal sealed class BookingRepository(BusBookingDbContext db) : IBookingRepository
{
    public Task<BookingAggregate?> GetByIdAsync(Guid bookingId, CancellationToken ct = default) =>
        db.Bookings.FirstOrDefaultAsync(b => b.Id == bookingId, ct);

    public async Task<IReadOnlyList<BookingAggregate>> GetByUserIdAsync(Guid userId, CancellationToken ct = default) =>
        await db.Bookings
                .Where(b => b.UserId == userId)
                .OrderByDescending(b => b.BookedAt)
                .ToListAsync(ct);

    public async Task AddAsync(BookingAggregate booking, CancellationToken ct = default) =>
        await db.Bookings.AddAsync(booking, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        db.SaveChangesAsync(ct);
}
