using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AgenticWorkforce.Infrastructure.Repositories;

internal sealed class ApiKeyRepository(AppDbContext db) : IApiKeyRepository
{
    public async Task<ApiKey> AddAsync(ApiKey key, CancellationToken ct = default)
    {
        db.ApiKeys.Add(key);
        await db.SaveChangesAsync(ct);
        return key;
    }

    public async Task<IReadOnlyList<ApiKey>> ListByUserAsync(Guid userId, CancellationToken ct = default)
        => await db.ApiKeys
            .AsNoTracking()
            .Where(k => k.UserId == userId && k.RevokedAt == null)
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync(ct);

    public Task<ApiKey?> GetByIdForUserAsync(Guid keyId, Guid userId, CancellationToken ct = default)
        => db.ApiKeys.FirstOrDefaultAsync(k => k.Id == keyId && k.UserId == userId, ct);

    public async Task<bool> RevokeAsync(Guid keyId, Guid userId, CancellationToken ct = default)
    {
        var key = await db.ApiKeys.FirstOrDefaultAsync(
            k => k.Id == keyId && k.UserId == userId && k.RevokedAt == null, ct);
        if (key is null)
            return false;

        key.RevokedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }
}
