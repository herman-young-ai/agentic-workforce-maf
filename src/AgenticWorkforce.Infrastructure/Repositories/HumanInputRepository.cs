using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AgenticWorkforce.Infrastructure.Repositories;

internal sealed class HumanInputRepository(AppDbContext db) : IHumanInputRepository
{
    public Task<HumanInputRequest?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => db.HumanInputRequests
            .Include(h => h.WorkflowRun)
            .Include(h => h.Task)
            .FirstOrDefaultAsync(h => h.Id == id, ct);

    public async Task<IReadOnlyList<HumanInputRequest>> ListPendingByProjectAsync(
        Guid projectId,
        CancellationToken ct = default)
        => await db.HumanInputRequests
            .AsNoTracking()
            .Where(h => h.ProjectId == projectId && h.Status == HumanInputRequestStatus.Pending)
            .OrderBy(h => h.CreatedAt)
            .ToListAsync(ct);

    public async Task<RespondOutcome> RespondAsync(
        Guid requestId,
        HumanDecisionType decision,
        string? response,
        Guid responderId,
        CancellationToken ct = default)
    {
        var request = await db.HumanInputRequests
            .Include(h => h.WorkflowRun)
            .FirstOrDefaultAsync(h => h.Id == requestId, ct);

        if (request is null)
            return new RespondOutcome(false, false, "Request not found.");

        if (request.Status != HumanInputRequestStatus.Pending)
            return new RespondOutcome(false, false,
                $"Request is already {request.Status.ToString().ToLowerInvariant()}.");

        // Principle 11 — Segregation of Duties. Only enforced when the
        // triggering user identity is typed (TriggeredById, not the legacy
        // string TriggeredBy). Schedule- and agent-triggered runs have no
        // owning human, so SOD is vacuous.
        if (request.WorkflowRun.TriggeredById.HasValue
            && request.WorkflowRun.TriggeredById.Value == responderId)
        {
            return new RespondOutcome(false, true,
                "Segregation of duties: the user who triggered this workflow run cannot respond to its human-input requests.");
        }

        request.Decision    = decision;
        request.Response    = response;
        request.ResponderId = responderId;
        request.Status      = HumanInputRequestStatus.Completed;
        request.ResolvedAt  = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return new RespondOutcome(true, false, null);
    }
}
