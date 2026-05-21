using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;

namespace AgenticWorkforce.Api.Features.Tasks;

public static class UpdateTask
{
    public record Request(
        string? Objective,
        string? AgentName,
        string? Inputs,
        int? MaxRetries);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapPatch("/api/v1/projects/{projectId:guid}/tasks/{taskId:guid}", HandleAsync)
            .RequireAuthorization(Policies.RequireOperator)
            .WithTags("Tasks")
            ;

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        Guid taskId,
        [FromBody] Request request,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        ITaskRepository repo,
        AppDbContext db,
        CancellationToken ct)
    {
        var user = userAccessor.User;
        await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Operator, ct);

        var task = await repo.GetByIdAsync(taskId, ct)
            ?? throw new NotFoundException("Task", taskId);

        if (task.ProjectId != projectId)
            throw new NotFoundException("Task", taskId);

        if (task.Status != TaskStatus.Proposed)
            throw new InvalidStateException($"Only proposed tasks can be updated (current status: {task.Status}).");

        if (request.Objective is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Objective))
                throw new ValidationException("Task objective cannot be empty.");
            task.Objective = request.Objective;
        }

        if (request.AgentName is not null) task.AgentName = request.AgentName;
        if (request.Inputs is not null) task.Inputs = request.Inputs;
        if (request.MaxRetries.HasValue) task.MaxRetries = request.MaxRetries.Value;

        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }
}
