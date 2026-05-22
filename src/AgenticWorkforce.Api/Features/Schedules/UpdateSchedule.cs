using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace AgenticWorkforce.Api.Features.Schedules;

public static class UpdateSchedule
{
    public record Request(string? CronExpression, bool? Enabled, DateTime? NextRunAt);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapPatch("/api/v1/projects/{projectId:guid}/schedules/{scheduleId:guid}", HandleAsync)
            .RequireAuthorization(Policies.RequireOwner)
            .WithTags("Schedules");

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        Guid scheduleId,
        [FromBody] Request request,
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

        if (request.CronExpression is not null)
        {
            if (string.IsNullOrWhiteSpace(request.CronExpression))
                throw new ValidationException("CronExpression cannot be empty.");
            schedule.CronExpression = request.CronExpression;
        }
        if (request.Enabled.HasValue) schedule.Enabled = request.Enabled.Value;
        if (request.NextRunAt.HasValue) schedule.NextRunAt = request.NextRunAt.Value;

        await repo.UpdateAsync(schedule, ct);
        return Results.NoContent();
    }
}
