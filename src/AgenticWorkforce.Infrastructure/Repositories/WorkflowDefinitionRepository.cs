using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Pagination;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AgenticWorkforce.Infrastructure.Repositories;

internal sealed class WorkflowDefinitionRepository(AppDbContext db) : IWorkflowDefinitionRepository
{
    public Task<WorkflowDefinition?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => db.WorkflowDefinitions
            .Include(w => w.Schedules)
            .FirstOrDefaultAsync(w => w.Id == id, ct);

    public Task<PagedResult<WorkflowDefinition>> ListByProjectPagedAsync(
        Guid projectId,
        PagedQuery paging,
        CancellationToken ct = default)
        => db.WorkflowDefinitions
            .AsNoTracking()
            .Where(w => w.ProjectId == projectId && w.LockedAt == null)
            .OrderByDescending(w => w.CreatedAt)
            .ToPagedResultAsync(paging, ct);

    public async Task<WorkflowDefinition> AddAsync(WorkflowDefinition definition, CancellationToken ct = default)
    {
        db.WorkflowDefinitions.Add(definition);
        await db.SaveChangesAsync(ct);
        return definition;
    }

    public async Task UpdateAsync(WorkflowDefinition definition, CancellationToken ct = default)
    {
        db.WorkflowDefinitions.Update(definition);
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> LockAsync(Guid id, CancellationToken ct = default)
    {
        var def = await db.WorkflowDefinitions.FirstOrDefaultAsync(w => w.Id == id, ct);
        if (def is null) return false;
        if (def.LockedAt is not null) return true;

        def.LockedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }
}
