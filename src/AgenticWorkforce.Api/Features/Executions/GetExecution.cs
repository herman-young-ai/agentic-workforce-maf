using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;

namespace AgenticWorkforce.Api.Features.Executions;

public static class GetExecution
{
    public record Response(Guid ExecutionId, Guid ProjectId, ExecutionState State);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/projects/{projectId:guid}/executions/{executionId:guid}", HandleAsync)
            .RequireAuthorization(Policies.RequireViewer)
            .WithTags("Executions");

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        Guid executionId,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        IExecutionRepository executions,
        CancellationToken ct)
    {
        await authz.EnsureRoleAsync(userAccessor.User.Id, projectId, ProjectRole.Viewer, ct);

        var status = await executions.GetStatusAsync(executionId, ct)
            ?? throw new NotFoundException("Execution", executionId);
        if (status.ProjectId != projectId)
            throw new NotFoundException("Execution", executionId);

        return Results.Ok(new Response(status.ExecutionId, status.ProjectId, status.State));
    }
}
