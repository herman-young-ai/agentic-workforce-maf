using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Pagination;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AgenticWorkforce.Infrastructure.Repositories;

internal sealed class WorkflowRunRepository(AppDbContext db) : IWorkflowRunRepository
{
    public Task<WorkflowRun?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => db.WorkflowRuns
            .Include(r => r.Tasks)
            .Include(r => r.HumanInputRequests)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<PagedResult<WorkflowRun>> ListByProjectPagedAsync(
        Guid projectId,
        Guid? workflowDefinitionId,
        PagedQuery paging,
        CancellationToken ct = default)
    {
        var query = db.WorkflowRuns
            .AsNoTracking()
            .Where(r => r.ProjectId == projectId);

        if (workflowDefinitionId.HasValue)
            query = query.Where(r => r.WorkflowDefinitionId == workflowDefinitionId.Value);

        return query
            .OrderByDescending(r => r.CreatedAt)
            .ToPagedResultAsync(paging, ct);
    }
}
