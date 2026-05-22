using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Pagination;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AgenticWorkforce.Infrastructure.Repositories;

internal sealed class EventRepository(AppDbContext db) : IEventRepository
{
    public Task<PagedResult<ProjectEvent>> ListByProjectPagedAsync(
        Guid projectId,
        EventFilter filter,
        PagedQuery paging,
        CancellationToken ct = default)
    {
        // Default 7-day window for partition pruning when caller omits bounds.
        // The partitioned table is RANGE-partitioned by created_at — without a
        // bound PostgreSQL has to scan every monthly partition.
        var since = filter.Since ?? DateTime.UtcNow.AddDays(-7);
        var until = filter.Until ?? DateTime.UtcNow.AddMinutes(1);

        var query = db.ProjectEvents
            .AsNoTracking()
            .Where(e => e.ProjectId == projectId
                     && e.CreatedAt >= since
                     && e.CreatedAt <= until);

        if (filter.MinSeverity.HasValue)
            query = query.Where(e => e.Severity >= filter.MinSeverity.Value);
        if (!string.IsNullOrEmpty(filter.EventType))
            query = query.Where(e => e.EventType == filter.EventType);
        if (filter.TaskId.HasValue)
            query = query.Where(e => e.TaskId == filter.TaskId.Value);
        if (filter.SessionId.HasValue)
            query = query.Where(e => e.SessionId == filter.SessionId.Value);

        return query
            .OrderByDescending(e => e.CreatedAt)
            .ToPagedResultAsync(paging, ct);
    }
}
