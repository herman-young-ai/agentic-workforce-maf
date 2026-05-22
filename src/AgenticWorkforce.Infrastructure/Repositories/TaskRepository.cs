using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Pagination;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AgenticWorkforce.Infrastructure.Repositories;

internal sealed class TaskRepository(AppDbContext db) : ITaskRepository
{
    public Task<AgenticTask?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => db.Tasks
            .Include(t => t.Attempts)
            .Include(t => t.Dependencies)
            .FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<IReadOnlyList<AgenticTask>> ListByProjectAsync(
        Guid projectId,
        TaskStatus? statusFilter = null,
        CancellationToken ct = default)
    {
        var query = db.Tasks
            .AsNoTracking()
            .Where(t => t.ProjectId == projectId);

        if (statusFilter.HasValue)
            query = query.Where(t => t.Status == statusFilter.Value);

        return await query.OrderBy(t => t.CreatedAt).ToListAsync(ct);
    }

    public Task<PagedResult<AgenticTask>> ListByProjectPagedAsync(
        Guid projectId,
        TaskListFilter filter,
        PagedQuery paging,
        CancellationToken ct = default)
    {
        var query = db.Tasks
            .AsNoTracking()
            .Where(t => t.ProjectId == projectId);

        if (filter.Status.HasValue)
            query = query.Where(t => t.Status == filter.Status.Value);
        if (filter.Type.HasValue)
            query = query.Where(t => t.Type == filter.Type.Value);
        if (filter.Source.HasValue)
            query = query.Where(t => t.Source == filter.Source.Value);
        if (!string.IsNullOrEmpty(filter.AgentName))
            query = query.Where(t => t.AgentName == filter.AgentName);
        if (filter.ParentTaskId.HasValue)
            query = query.Where(t => t.ParentTaskId == filter.ParentTaskId.Value);

        return query
            .OrderByDescending(t => t.CreatedAt)
            .ToPagedResultAsync(paging, ct);
    }

    public async Task<IReadOnlyList<AgenticTask>> GetBoardAsync(Guid projectId, CancellationToken ct = default)
        => await db.Tasks
            .AsNoTracking()
            .Where(t => t.ProjectId == projectId)
            .Include(t => t.Dependencies)
            .Include(t => t.Dependents)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync(ct);

    public Task<int> CountByProjectAsync(
        Guid projectId,
        IReadOnlyList<TaskStatus>? statuses = null,
        CancellationToken ct = default)
    {
        var query = db.Tasks.AsNoTracking().Where(t => t.ProjectId == projectId);
        if (statuses is { Count: > 0 })
            query = query.Where(t => statuses.Contains(t.Status));
        return query.CountAsync(ct);
    }

    public async Task<AgenticTask> AddAsync(AgenticTask task, CancellationToken ct = default)
    {
        db.Tasks.Add(task);
        await db.SaveChangesAsync(ct);
        return task;
    }

    public async Task UpdateAsync(AgenticTask task, CancellationToken ct = default)
    {
        db.Tasks.Update(task);
        await db.SaveChangesAsync(ct);
    }

    public async Task<BulkApproveResult> BulkApproveAsync(
        Guid projectId,
        IReadOnlyList<Guid> taskIds,
        Guid approverId,
        CancellationToken ct = default)
    {
        var tasks = await db.Tasks
            .Where(t => t.ProjectId == projectId && taskIds.Contains(t.Id))
            .ToListAsync(ct);

        var items = new List<BulkApproveItem>();

        foreach (var task in tasks)
        {
            if (task.Status != TaskStatus.Proposed)
            {
                items.Add(new BulkApproveItem(task.Id, false,
                    $"Task is not in Proposed status (current: {task.Status})."));
                continue;
            }

            if (task.CreatedById.HasValue && task.CreatedById == approverId)
            {
                items.Add(new BulkApproveItem(task.Id, false,
                    "Segregation of duties: creator cannot approve their own task."));
                continue;
            }

            task.Status = TaskStatus.Approved;
            items.Add(new BulkApproveItem(task.Id, true, null));
        }

        foreach (var missingId in taskIds.Except(tasks.Select(t => t.Id)))
            items.Add(new BulkApproveItem(missingId, false, "Task not found in this project."));

        await db.SaveChangesAsync(ct);

        return new BulkApproveResult(items);
    }
}
