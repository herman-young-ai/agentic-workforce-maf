using AgenticWorkforce.Domain.Entities;

namespace AgenticWorkforce.Domain.Interfaces.Repositories;

/// <summary>
/// Query-only abstraction for the Session aggregate. Writes go through
/// <c>AppDbContext.Sessions</c> directly from vertical-slice handlers.
/// </summary>
public interface ISessionRepository
{
    Task<Session?> GetByIdAsync(Guid id, CancellationToken ct = default);
}
