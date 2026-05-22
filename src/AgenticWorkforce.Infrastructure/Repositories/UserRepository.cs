using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AgenticWorkforce.Infrastructure.Repositories;

internal sealed class UserRepository(AppDbContext db) : IUserRepository
{
    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

    public async Task UpdateAsync(User user, CancellationToken ct = default)
    {
        db.Users.Update(user);
        await db.SaveChangesAsync(ct);
    }

    public async Task<(User User, bool Created)> EnsureProvisionedAsync(
        Guid id,
        string email,
        string displayName,
        CancellationToken ct = default)
    {
        var existing = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (existing is not null)
            return (existing, false);

        var user = new User
        {
            Id          = id,
            Email       = email,
            DisplayName = displayName,
            SystemRole  = SystemRole.Member,
            IsActive    = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return (user, true);
    }
}
