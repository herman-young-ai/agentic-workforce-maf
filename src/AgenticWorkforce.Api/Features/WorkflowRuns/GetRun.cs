using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;

namespace AgenticWorkforce.Api.Features.WorkflowRuns;

public static class GetRun
{
    public record TaskSummary(Guid Id, string Objective, TaskStatus Status, decimal CostUsd);

    public record Response(
        Guid Id,
        Guid ProjectId,
        Guid WorkflowDefinitionId,
        string WorkflowName,
        int WorkflowVersion,
        WorkflowRunStatus Status,
        Guid? TriggeredById,
        string? TriggeredBy,
        decimal TotalCostUsd,
        decimal? BudgetUsd,
        IReadOnlyList<TaskSummary> Tasks,
        DateTime CreatedAt,
        DateTime UpdatedAt);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/projects/{projectId:guid}/workflow-runs/{runId:guid}", HandleAsync)
            .RequireAuthorization(Policies.RequireViewer)
            .WithTags("WorkflowRuns");

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        Guid runId,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        IWorkflowRunRepository runs,
        CancellationToken ct)
    {
        await authz.EnsureRoleAsync(userAccessor.User.Id, projectId, ProjectRole.Viewer, ct);

        var run = await runs.GetByIdAsync(runId, ct)
            ?? throw new NotFoundException("WorkflowRun", runId);
        if (run.ProjectId != projectId)
            throw new NotFoundException("WorkflowRun", runId);

        var tasks = run.Tasks
            .Select(t => new TaskSummary(t.Id, t.Objective, t.Status, t.CostUsd))
            .ToList();

        return Results.Ok(new Response(
            run.Id, run.ProjectId, run.WorkflowDefinitionId, run.WorkflowName, run.WorkflowVersion,
            run.Status, run.TriggeredById, run.TriggeredBy, run.TotalCostUsd, run.BudgetUsd,
            tasks, run.CreatedAt, run.UpdatedAt));
    }
}
