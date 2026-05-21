using QuotesApi.Models;

namespace QuotesApi.Repositories;

public interface IUserRepository
{
    Task<User?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<User?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<User> CreateAsync(User user, CancellationToken ct = default);
    Task AddRefreshTokenAsync(RefreshToken token, CancellationToken ct = default);
    Task<RefreshToken?> GetRefreshTokenByHashAsync(string hash, CancellationToken ct = default);
    Task RevokeTokenFamilyAsync(Guid familyId, CancellationToken ct = default);
    Task RevokeRefreshTokenAsync(RefreshToken token, CancellationToken ct = default);
}
