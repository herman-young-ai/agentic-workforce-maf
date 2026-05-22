using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;

namespace AgenticWorkforce.Api.Features.Workflows;

public static class GetWorkflow
{
    public record Response(
        Guid Id,
        Guid? ProjectId,
        string Name,
        string? Description,
        int Version,
        bool Enabled,
        string Nodes,
        string Edges,
        string? CanvasState,
        DateTime? LockedAt,
        DateTime CreatedAt,
        DateTime UpdatedAt);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/projects/{projectId:guid}/workflows/{workflowId:guid}", HandleAsync)
            .RequireAuthorization(Policies.RequireViewer)
            .WithTags("Workflows");

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        Guid workflowId,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        IWorkflowDefinitionRepository repo,
        CancellationToken ct)
    {
        await authz.EnsureRoleAsync(userAccessor.User.Id, projectId, ProjectRole.Viewer, ct);

        var w = await repo.GetByIdAsync(workflowId, ct)
            ?? throw new NotFoundException("Workflow", workflowId);

        if (w.ProjectId != projectId)
            throw new NotFoundException("Workflow", workflowId);

        return Results.Ok(new Response(
            w.Id, w.ProjectId, w.Name, w.Description, w.Version, w.Enabled,
            w.Nodes, w.Edges, w.CanvasState, w.LockedAt, w.CreatedAt, w.UpdatedAt));
    }
}
