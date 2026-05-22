using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Pagination;
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

    public Task<bool> ExistsInProjectAsync(Guid sessionId, Guid projectId, CancellationToken ct = default)
        => db.Sessions
            .AsNoTracking()
            .AnyAsync(s => s.Id == sessionId && s.ProjectId == projectId, ct);

    public Task<PagedResult<Session>> ListByProjectPagedAsync(
        Guid projectId,
        SessionStatus? statusFilter,
        PagedQuery paging,
        CancellationToken ct = default)
    {
        var query = db.Sessions
            .AsNoTracking()
            .Where(s => s.ProjectId == projectId);

        if (statusFilter.HasValue)
            query = query.Where(s => s.Status == statusFilter.Value);

        return query
            .OrderByDescending(s => s.CreatedAt)
            .ToPagedResultAsync(paging, ct);
    }

    public Task<PagedResult<SessionMessage>> ListMessagesPagedAsync(
        Guid sessionId,
        PagedQuery paging,
        CancellationToken ct = default)
        => db.SessionMessages
            .AsNoTracking()
            .Where(m => m.SessionId == sessionId)
            .OrderBy(m => m.CreatedAt)
            .ToPagedResultAsync(paging, ct);

    public async Task<Session> AddAsync(Session session, CancellationToken ct = default)
    {
        db.Sessions.Add(session);
        await db.SaveChangesAsync(ct);
        return session;
    }

    public async Task UpdateAsync(Session session, CancellationToken ct = default)
    {
        db.Sessions.Update(session);
        await db.SaveChangesAsync(ct);
    }
}
