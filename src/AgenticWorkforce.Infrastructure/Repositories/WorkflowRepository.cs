using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AgenticWorkforce.Infrastructure.Repositories;

internal sealed class WorkflowRepository(AppDbContext db) : IWorkflowRepository
{
    public Task<WorkflowDefinition?> GetDefinitionByIdAsync(Guid id, CancellationToken ct = default)
        => db.WorkflowDefinitions
            .Include(w => w.Schedules)
            .FirstOrDefaultAsync(w => w.Id == id, ct);

    public Task<WorkflowRun?> GetRunByIdAsync(Guid id, CancellationToken ct = default)
        => db.WorkflowRuns
            .Include(r => r.Tasks)
            .Include(r => r.HumanInputRequests)
            .FirstOrDefaultAsync(r => r.Id == id, ct);
}
