using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Pagination;

namespace AgenticWorkforce.Domain.Interfaces.Repositories;

/// <summary>
/// Repository for the Session aggregate. Includes session messages because
/// they share the session lifecycle and read paths.
/// </summary>
public interface ISessionRepository
{
    Task<Session?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Lightweight ownership check — true if the session exists AND belongs to
    /// the project. Used by message-listing endpoints to verify the
    /// (projectId, sessionId) tuple without paying to materialise the session's
    /// messages and channels (which <see cref="GetByIdAsync"/> includes).
    /// </summary>
    Task<bool> ExistsInProjectAsync(Guid sessionId, Guid projectId, CancellationToken ct = default);

    Task<PagedResult<Session>> ListByProjectPagedAsync(
        Guid projectId,
        SessionStatus? statusFilter,
        PagedQuery paging,
        CancellationToken ct = default);

    Task<PagedResult<SessionMessage>> ListMessagesPagedAsync(
        Guid sessionId,
        PagedQuery paging,
        CancellationToken ct = default);

    Task<Session> AddAsync(Session session, CancellationToken ct = default);

    Task UpdateAsync(Session session, CancellationToken ct = default);
}
