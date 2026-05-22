using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace AgenticWorkforce.Api.Features.Schedules;

public static class CreateSchedule
{
    public record Request(Guid WorkflowDefinitionId, string CronExpression, bool Enabled = true, DateTime? NextRunAt = null);

    public record Response(Guid Id, DateTime CreatedAt);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapPost("/api/v1/projects/{projectId:guid}/schedules", HandleAsync)
            .RequireAuthorization(Policies.RequireOwner)
            .WithTags("Schedules");

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        [FromBody] Request request,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        IWorkflowDefinitionRepository workflows,
        IWorkflowScheduleRepository repo,
        CancellationToken ct)
    {
        await authz.EnsureRoleAsync(userAccessor.User.Id, projectId, ProjectRole.Owner, ct);

        if (string.IsNullOrWhiteSpace(request.CronExpression))
            throw new ValidationException("CronExpression is required.");

        var workflow = await workflows.GetByIdAsync(request.WorkflowDefinitionId, ct)
            ?? throw new NotFoundException("Workflow", request.WorkflowDefinitionId);
        if (workflow.ProjectId != projectId)
            throw new NotFoundException("Workflow", request.WorkflowDefinitionId);

        var schedule = await repo.AddAsync(new WorkflowSchedule
        {
            ProjectId            = projectId,
            WorkflowDefinitionId = request.WorkflowDefinitionId,
            CronExpression       = request.CronExpression,
            Enabled              = request.Enabled,
            NextRunAt            = request.NextRunAt
        }, ct);

        return Results.Created(
            $"/api/v1/projects/{projectId}/schedules/{schedule.Id}",
            new Response(schedule.Id, schedule.CreatedAt));
    }
}
