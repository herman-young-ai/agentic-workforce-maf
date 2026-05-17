using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AgenticWorkforce.Infrastructure.Repositories;

internal sealed class TaskRepository(AppDbContext db) : ITaskRepository
{
    public async Task<AgenticTask?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await db.Tasks
            .Include(t => t.Attempts)
            .Include(t => t.Dependencies)
            .FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<IReadOnlyList<AgenticTask>> GetByProjectIdAsync(
        Guid projectId,
        TaskStatus? status = null,
        CancellationToken ct = default)
    {
        var query = db.Tasks.Where(t => t.ProjectId == projectId);
        if (status.HasValue)
            query = query.Where(t => t.Status == status.Value);
        return await query.OrderBy(t => t.CreatedAt).ToListAsync(ct);
    }

    public async Task<AgenticTask> CreateAsync(AgenticTask task, CancellationToken ct = default)
    {
        db.Tasks.Add(task);
        await db.SaveChangesAsync(ct);
        return task;
    }

    public async Task<AgenticTask> UpdateAsync(AgenticTask task, CancellationToken ct = default)
    {
        db.Tasks.Update(task);
        await db.SaveChangesAsync(ct);
        return task;
    }

    public async Task<IReadOnlyList<AgenticTask>> GetBoardAsync(Guid projectId, CancellationToken ct = default)
        => await db.Tasks
            .Where(t => t.ProjectId == projectId)
            .Include(t => t.Dependencies)
            .Include(t => t.Dependents)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync(ct);
}
