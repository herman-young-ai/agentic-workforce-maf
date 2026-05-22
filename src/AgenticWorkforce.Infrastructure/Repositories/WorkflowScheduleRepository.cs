using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AgenticWorkforce.Infrastructure.Repositories;

internal sealed class WorkflowScheduleRepository(AppDbContext db) : IWorkflowScheduleRepository
{
    public Task<WorkflowSchedule?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => db.WorkflowSchedules.FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<IReadOnlyList<WorkflowSchedule>> ListByProjectAsync(
        Guid projectId,
        CancellationToken ct = default)
        => await db.WorkflowSchedules
            .AsNoTracking()
            .Where(s => s.ProjectId == projectId)
            .OrderBy(s => s.NextRunAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<WorkflowSchedule>> ListUpcomingAsync(
        Guid projectId,
        TimeSpan horizon,
        CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.Add(horizon);
        return await db.WorkflowSchedules
            .AsNoTracking()
            .Where(s => s.ProjectId == projectId
                     && s.Enabled
                     && s.NextRunAt != null
                     && s.NextRunAt <= cutoff)
            .OrderBy(s => s.NextRunAt)
            .ToListAsync(ct);
    }

    public async Task<WorkflowSchedule> AddAsync(WorkflowSchedule schedule, CancellationToken ct = default)
    {
        db.WorkflowSchedules.Add(schedule);
        await db.SaveChangesAsync(ct);
        return schedule;
    }

    public async Task UpdateAsync(WorkflowSchedule schedule, CancellationToken ct = default)
    {
        db.WorkflowSchedules.Update(schedule);
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> RemoveAsync(Guid id, CancellationToken ct = default)
    {
        var sched = await db.WorkflowSchedules.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (sched is null) return false;

        db.WorkflowSchedules.Remove(sched);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
