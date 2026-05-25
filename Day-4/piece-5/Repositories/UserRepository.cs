using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Repositories;

public sealed class UserRepository(AppDbContext db) : IUserRepository
{
    public Task<User?> GetByEmailAsync(string email, CancellationToken ct = default) =>
        db.Users.FirstOrDefaultAsync(u => u.Email == email.ToLowerInvariant(), ct);

    public Task<User?> GetByIdAsync(int id, CancellationToken ct = default) =>
        db.Users.FindAsync([id], ct).AsTask()!;

    public async Task AddRefreshTokenAsync(RefreshToken token, CancellationToken ct = default)
    {
        db.RefreshTokens.Add(token);
        await db.SaveChangesAsync(ct);
    }

    public Task<RefreshToken?> GetRefreshTokenByHashAsync(string hash, CancellationToken ct = default) =>
        db.RefreshTokens.FirstOrDefaultAsync(r => r.Token == hash, ct);

    public async Task RevokeTokenFamilyAsync(Guid familyId, CancellationToken ct = default)
    {
        var activeTokens = await db.RefreshTokens
            .Where(r => r.FamilyId == familyId && r.RevokedAt == null)
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        foreach (var t in activeTokens)
            t.Revoke(now);

        await db.SaveChangesAsync(ct);
    }

    public async Task RevokeRefreshTokenAsync(RefreshToken token, CancellationToken ct = default)
    {
        token.Revoke();
        await db.SaveChangesAsync(ct);
    }
}
