using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;

namespace AgenticWorkforce.Api.Features.Schedules;

public static class DeleteSchedule
{
    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapDelete("/api/v1/projects/{projectId:guid}/schedules/{scheduleId:guid}", HandleAsync)
            .RequireAuthorization(Policies.RequireOwner)
            .WithTags("Schedules");

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        Guid scheduleId,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        IWorkflowScheduleRepository repo,
        CancellationToken ct)
    {
        await authz.EnsureRoleAsync(userAccessor.User.Id, projectId, ProjectRole.Owner, ct);

        var schedule = await repo.GetByIdAsync(scheduleId, ct)
            ?? throw new NotFoundException("Schedule", scheduleId);
        if (schedule.ProjectId != projectId)
            throw new NotFoundException("Schedule", scheduleId);

        await repo.RemoveAsync(scheduleId, ct);
        return Results.NoContent();
    }
}
