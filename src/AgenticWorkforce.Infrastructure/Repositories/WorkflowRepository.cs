using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AgenticWorkforce.Infrastructure.Repositories;

internal sealed class WorkflowRepository(AppDbContext db) : IWorkflowRepository
{
    public async Task<WorkflowDefinition?> GetDefinitionByIdAsync(Guid id, CancellationToken ct = default)
        => await db.WorkflowDefinitions
            .Include(w => w.Schedules)
            .FirstOrDefaultAsync(w => w.Id == id, ct);

    public async Task<WorkflowDefinition> CreateDefinitionAsync(WorkflowDefinition definition, CancellationToken ct = default)
    {
        db.WorkflowDefinitions.Add(definition);
        await db.SaveChangesAsync(ct);
        return definition;
    }

    public async Task<WorkflowDefinition> UpdateDefinitionAsync(WorkflowDefinition definition, CancellationToken ct = default)
    {
        db.WorkflowDefinitions.Update(definition);
        await db.SaveChangesAsync(ct);
        return definition;
    }

    public async Task<WorkflowRun?> GetRunByIdAsync(Guid id, CancellationToken ct = default)
        => await db.WorkflowRuns
            .Include(r => r.Tasks)
            .Include(r => r.HumanInputRequests)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<WorkflowRun> CreateRunAsync(WorkflowRun run, CancellationToken ct = default)
    {
        db.WorkflowRuns.Add(run);
        await db.SaveChangesAsync(ct);
        return run;
    }

    public async Task<WorkflowRun> UpdateRunAsync(WorkflowRun run, CancellationToken ct = default)
    {
        db.WorkflowRuns.Update(run);
        await db.SaveChangesAsync(ct);
        return run;
    }
}
