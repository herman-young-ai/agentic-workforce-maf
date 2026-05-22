using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Interfaces.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgenticWorkforce.Api.Features.Workflows;

public static class CreateWorkflow
{
    public record Request(
        string Name,
        string? Description,
        string Nodes,
        string Edges,
        string? CanvasState);

    public record Response(Guid Id, string Name, int Version, DateTime CreatedAt);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapPost("/api/v1/projects/{projectId:guid}/workflows", HandleAsync)
            .RequireAuthorization(Policies.RequireOperator)
            .WithTags("Workflows");

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        [FromBody] Request request,
        [FromHeader(Name = "X-Idempotency-Key")] string? idempotencyKey,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        IIdempotencyService idempotency,
        IWorkflowDefinitionRepository repo,
        IWorkflowValidator validator,
        CancellationToken ct)
    {
        var user = userAccessor.User;
        await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Operator, ct);

        if (idempotencyKey is not null)
        {
            var cached = await idempotency.GetCachedResponseAsync<Response>(user.Id, idempotencyKey, ct);
            if (cached is not null)
                return Results.Created($"/api/v1/projects/{projectId}/workflows/{cached.Id}", cached);
        }

        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ValidationException("Workflow name is required.");

        var result = validator.Validate(request.Nodes, request.Edges);
        if (!result.IsValid)
            throw new ValidationException(
                "Workflow graph is invalid: " + string.Join("; ", result.Errors.Select(e => e.Message)));

        var workflow = await repo.AddAsync(new WorkflowDefinition
        {
            ProjectId     = projectId,
            Name          = request.Name,
            Description   = request.Description,
            Version       = 1,
            Enabled       = true,
            Nodes         = request.Nodes,
            Edges         = request.Edges,
            CanvasState   = request.CanvasState,
            DesignedBy    = user.Email,
            FormatVersion = "1.0"
        }, ct);

        var response = new Response(workflow.Id, workflow.Name, workflow.Version, workflow.CreatedAt);

        if (idempotencyKey is not null)
            await idempotency.CacheResponseAsync(user.Id, idempotencyKey, response, ct);

        return Results.Created($"/api/v1/projects/{projectId}/workflows/{workflow.Id}", response);
    }
}
