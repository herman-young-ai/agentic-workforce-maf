using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AgenticWorkforce.Infrastructure.Repositories;

internal sealed class SessionRepository(AppDbContext db) : ISessionRepository
{
    public Task<Session?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => db.Sessions
            .Include(s => s.Messages)
            .Include(s => s.Channels)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
}
