using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;

namespace AgenticWorkforce.Api.Features.Tasks;

public static class GetTask
{
    public record AttemptSummary(int AttemptNumber, AttemptStatus Status, string? FailureReason, decimal CostUsd);

    public record Response(
        Guid Id,
        Guid ProjectId,
        TaskType Type,
        TaskStatus Status,
        string Objective,
        string? AgentName,
        TaskSource Source,
        string? Inputs,
        string? Outputs,
        string? OutputSummary,
        decimal CostUsd,
        DateTime? StartedAt,
        DateTime? CompletedAt,
        double? DurationSeconds,
        int RetryCount,
        int MaxRetries,
        Guid? ParentTaskId,
        Guid? AssignedToId,
        Guid? CreatedById,
        IReadOnlyList<AttemptSummary> Attempts,
        DateTime CreatedAt,
        DateTime UpdatedAt);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/projects/{projectId:guid}/tasks/{taskId:guid}", HandleAsync)
            .RequireAuthorization(Policies.RequireViewer)
            .WithTags("Tasks")
            ;

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        Guid taskId,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        ITaskRepository repo,
        CancellationToken ct)
    {
        var user = userAccessor.User;
        await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Viewer, ct);

        var task = await repo.GetByIdAsync(taskId, ct)
            ?? throw new NotFoundException("Task", taskId);

        if (task.ProjectId != projectId)
            throw new NotFoundException("Task", taskId);

        var attempts = task.Attempts
            .Select(a => new AttemptSummary(a.AttemptNumber, a.Status, a.FailureReason, a.CostUsd))
            .ToList();

        return Results.Ok(new Response(
            task.Id, task.ProjectId, task.Type, task.Status, task.Objective,
            task.AgentName, task.Source, task.Inputs, task.Outputs, task.OutputSummary,
            task.CostUsd, task.StartedAt, task.CompletedAt, task.DurationSeconds,
            task.RetryCount, task.MaxRetries, task.ParentTaskId, task.AssignedToId,
            task.CreatedById, attempts, task.CreatedAt, task.UpdatedAt));
    }
}
