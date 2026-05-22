using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;

namespace AgenticWorkforce.Api.Features.Workflows;

public static class RunWorkflow
{
    public record Response(Guid ExecutionId, string Status);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapPost("/api/v1/projects/{projectId:guid}/workflows/{workflowId:guid}/run", HandleAsync)
            .RequireAuthorization(Policies.RequireOperator)
            .WithTags("Workflows");

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        Guid workflowId,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        IWorkflowDefinitionRepository workflows,
        IExecutionRepository executions,
        CancellationToken ct)
    {
        var user = userAccessor.User;
        await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Operator, ct);

        var w = await workflows.GetByIdAsync(workflowId, ct)
            ?? throw new NotFoundException("Workflow", workflowId);
        if (w.ProjectId != projectId)
            throw new NotFoundException("Workflow", workflowId);
        if (w.LockedAt is not null || !w.Enabled)
            throw new InvalidStateException("Workflow is locked or disabled.");

        // Encode workflow id as a single-element "task list" for the queue.
        // Worker (Phase 8) reads the message and creates the WorkflowRun row;
        // Api never writes WorkflowRun directly (Principle 16).
        var executionId = await executions.EnqueueDispatchAsync(
            projectId, [workflowId], user.Id, ct);

        return Results.Accepted(
            $"/api/v1/projects/{projectId}/executions/{executionId}",
            new Response(executionId, "Pending"));
    }
}
