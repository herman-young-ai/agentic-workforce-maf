using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace AgenticWorkforce.Api.Features.Schedules;

public static class ListUpcoming
{
    public record Response(
        Guid Id,
        Guid WorkflowDefinitionId,
        string CronExpression,
        DateTime? NextRunAt);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/projects/{projectId:guid}/schedules/upcoming", HandleAsync)
            .RequireAuthorization(Policies.RequireViewer)
            .WithTags("Schedules");

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        [FromQuery] int? horizonHours,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        IWorkflowScheduleRepository repo,
        CancellationToken ct)
    {
        await authz.EnsureRoleAsync(userAccessor.User.Id, projectId, ProjectRole.Viewer, ct);

        var horizon = TimeSpan.FromHours(Math.Clamp(horizonHours ?? 24, 1, 24 * 30));
        var list = await repo.ListUpcomingAsync(projectId, horizon, ct);

        return Results.Ok(list.Select(s =>
            new Response(s.Id, s.WorkflowDefinitionId, s.CronExpression, s.NextRunAt)).ToList());
    }
}
