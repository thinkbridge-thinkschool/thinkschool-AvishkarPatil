using QuotesApi.Models;

namespace QuotesApi.Repositories;

public interface ICollectionRepository
{
    Task<Collection?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<Collection> CreateAsync(Collection collection, CancellationToken cancellationToken = default);
    Task<bool> UpdateAsync(Collection collection, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
}
