using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Interfaces.Repositories;

namespace AgenticWorkforce.Api.Features.Schedules;

public static class ListSchedules
{
    public record Response(
        Guid Id,
        Guid WorkflowDefinitionId,
        string CronExpression,
        bool Enabled,
        DateTime? NextRunAt,
        DateTime? LastRunAt);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/projects/{projectId:guid}/schedules", HandleAsync)
            .RequireAuthorization(Policies.RequireViewer)
            .WithTags("Schedules");

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        IWorkflowScheduleRepository repo,
        CancellationToken ct)
    {
        await authz.EnsureRoleAsync(userAccessor.User.Id, projectId, ProjectRole.Viewer, ct);
        var list = await repo.ListByProjectAsync(projectId, ct);
        return Results.Ok(list.Select(s =>
            new Response(s.Id, s.WorkflowDefinitionId, s.CronExpression, s.Enabled, s.NextRunAt, s.LastRunAt)).ToList());
    }
}
