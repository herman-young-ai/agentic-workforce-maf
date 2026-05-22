using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace AgenticWorkforce.Api.Features.Workflows;

public static class SaveCanvas
{
    public record Request(string CanvasState);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapPut("/api/v1/projects/{projectId:guid}/workflows/{workflowId:guid}/canvas", HandleAsync)
            .RequireAuthorization(Policies.RequireOperator)
            .WithTags("Workflows");

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        Guid workflowId,
        [FromBody] Request request,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        IWorkflowDefinitionRepository repo,
        CancellationToken ct)
    {
        await authz.EnsureRoleAsync(userAccessor.User.Id, projectId, ProjectRole.Operator, ct);

        if (string.IsNullOrWhiteSpace(request.CanvasState))
            throw new ValidationException("CanvasState cannot be empty.");

        var w = await repo.GetByIdAsync(workflowId, ct)
            ?? throw new NotFoundException("Workflow", workflowId);
        if (w.ProjectId != projectId)
            throw new NotFoundException("Workflow", workflowId);

        if (w.LockedAt is not null)
            throw new InvalidStateException("Locked workflows cannot be modified.");

        w.CanvasState = request.CanvasState;
        await repo.UpdateAsync(w, ct);
        return Results.NoContent();
    }
}
