using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;

namespace AgenticWorkforce.Api.Features.Workflows;

public static class DeleteWorkflow
{
    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapDelete("/api/v1/projects/{projectId:guid}/workflows/{workflowId:guid}", HandleAsync)
            .RequireAuthorization(Policies.RequireOwner)
            .WithTags("Workflows");

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        Guid workflowId,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        IWorkflowDefinitionRepository repo,
        CancellationToken ct)
    {
        await authz.EnsureRoleAsync(userAccessor.User.Id, projectId, ProjectRole.Owner, ct);

        var w = await repo.GetByIdAsync(workflowId, ct)
            ?? throw new NotFoundException("Workflow", workflowId);
        if (w.ProjectId != projectId)
            throw new NotFoundException("Workflow", workflowId);

        // Soft delete (Principle 13): set LockedAt rather than dropping the row,
        // so historical WorkflowRuns retain a referenced definition.
        if (!await repo.LockAsync(workflowId, ct))
            throw new NotFoundException("Workflow", workflowId);

        return Results.NoContent();
    }
}
