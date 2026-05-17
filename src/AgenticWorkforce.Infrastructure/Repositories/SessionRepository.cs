using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AgenticWorkforce.Infrastructure.Repositories;

internal sealed class SessionRepository(AppDbContext db) : ISessionRepository
{
    public async Task<Session?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await db.Sessions
            .Include(s => s.Messages)
            .Include(s => s.Channels)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<Session> CreateAsync(Session session, CancellationToken ct = default)
    {
        db.Sessions.Add(session);
        await db.SaveChangesAsync(ct);
        return session;
    }

    public async Task<Session> UpdateAsync(Session session, CancellationToken ct = default)
    {
        db.Sessions.Update(session);
        await db.SaveChangesAsync(ct);
        return session;
    }
}
