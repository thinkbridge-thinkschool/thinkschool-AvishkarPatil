using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Repositories;

public class CollectionRepository : ICollectionRepository
{
    private readonly AppDbContext _context;

    public CollectionRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<Collection?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Collections
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public async Task<Collection> CreateAsync(Collection collection, CancellationToken cancellationToken = default)
    {
        _context.Collections.Add(collection);
        await _context.SaveChangesAsync(cancellationToken);
        return collection;
    }

    public async Task<bool> UpdateAsync(Collection collection, CancellationToken cancellationToken = default)
    {
        // Since we are using EF Core and tracking the entity,
        // we usually just need to call SaveChanges if we fetched it.
        // But if it's attached/detached, we can update it explicitly.
        _context.Collections.Update(collection);
        var changes = await _context.SaveChangesAsync(cancellationToken);
        return changes > 0;
    }

}
