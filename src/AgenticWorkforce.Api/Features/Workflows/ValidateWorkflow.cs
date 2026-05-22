using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Interfaces.Services;

namespace AgenticWorkforce.Api.Features.Workflows;

public static class ValidateWorkflow
{
    public record ErrorItem(string Cause, string Message);
    public record Response(bool IsValid, IReadOnlyList<ErrorItem> Errors);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapPost("/api/v1/projects/{projectId:guid}/workflows/{workflowId:guid}/validate", HandleAsync)
            .RequireAuthorization(Policies.RequireOperator)
            .WithTags("Workflows");

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        Guid workflowId,
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

        var result = validator.Validate(w.Nodes, w.Edges);
        return Results.Ok(new Response(
            result.IsValid,
            result.Errors.Select(e => new ErrorItem(e.Cause.ToString(), e.Message)).ToList()));
    }
}
