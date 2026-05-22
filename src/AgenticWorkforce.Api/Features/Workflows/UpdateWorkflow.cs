using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Interfaces.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgenticWorkforce.Api.Features.Workflows;

public static class UpdateWorkflow
{
    public record Request(string? Name, string? Description, string? Nodes, string? Edges, bool? Enabled);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapPatch("/api/v1/projects/{projectId:guid}/workflows/{workflowId:guid}", HandleAsync)
            .RequireAuthorization(Policies.RequireOperator)
            .WithTags("Workflows");

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        Guid workflowId,
        [FromBody] Request request,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        IWorkflowDefinitionRepository repo,
        IWorkflowValidator validator,
        CancellationToken ct)
    {
        await authz.EnsureRoleAsync(userAccessor.User.Id, projectId, ProjectRole.Operator, ct);

        var w = await repo.GetByIdAsync(workflowId, ct)
            ?? throw new NotFoundException("Workflow", workflowId);
        if (w.ProjectId != projectId)
            throw new NotFoundException("Workflow", workflowId);

        if (w.LockedAt is not null)
            throw new InvalidStateException("Locked workflows cannot be modified.");

        if (request.Name is not null) w.Name = request.Name;
        if (request.Description is not null) w.Description = request.Description;
        if (request.Enabled.HasValue) w.Enabled = request.Enabled.Value;

        var newNodes = request.Nodes ?? w.Nodes;
        var newEdges = request.Edges ?? w.Edges;
        if (request.Nodes is not null || request.Edges is not null)
        {
            var result = validator.Validate(newNodes, newEdges);
            if (!result.IsValid)
                throw new ValidationException(
                    "Workflow graph is invalid: " + string.Join("; ", result.Errors.Select(e => e.Message)));

            w.Nodes = newNodes;
            w.Edges = newEdges;
            w.Version += 1;
        }

        await repo.UpdateAsync(w, ct);
        return Results.NoContent();
    }
}
