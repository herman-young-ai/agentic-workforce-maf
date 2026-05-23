using System.Text.Json;
using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Events;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Interfaces.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgenticWorkforce.Api.Features.Tasks;

public static class CreateTask
{
    public record Request(
        string Objective,
        TaskType Type = TaskType.AgentTask,
        TaskSource Source = TaskSource.Manual,
        string? AgentName = null,
        string? Inputs = null,
        int MaxRetries = 3,
        Guid? ParentTaskId = null);

    public record Response(
        Guid Id,
        Guid ProjectId,
        TaskType Type,
        TaskStatus Status,
        string Objective,
        TaskSource Source,
        DateTime CreatedAt);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapPost("/api/v1/projects/{projectId:guid}/tasks", HandleAsync)
            .RequireAuthorization(Policies.RequireOperator)
            .WithTags("Tasks")
            ;

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        [FromBody] Request request,
        [FromHeader(Name = "X-Idempotency-Key")] string? idempotencyKey,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        IIdempotencyService idempotency,
        ITaskRepository repo,
        IEventPublisher publisher,
        CancellationToken ct)
    {
        var user = userAccessor.User;
        await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Operator, ct);

        if (idempotencyKey is not null)
        {
            var cached = await idempotency.GetCachedResponseAsync<Response>(user.Id, idempotencyKey, ct);
            if (cached is not null)
                return Results.Created($"/api/v1/projects/{projectId}/tasks/{cached.Id}", cached);
        }

        if (string.IsNullOrWhiteSpace(request.Objective))
            throw new ValidationException("Task objective is required.");

        var task = new AgenticTask
        {
            ProjectId     = projectId,
            Objective     = request.Objective,
            Type          = request.Type,
            Source        = request.Source,
            Status        = TaskStatus.Proposed,
            AgentName     = request.AgentName,
            Inputs        = request.Inputs,
            MaxRetries    = request.MaxRetries,
            ParentTaskId  = request.ParentTaskId,
            CreatedById   = user.Id,
            FormatVersion = "1.0"
        };

        await publisher.PublishAsync(new ProjectEvent
        {
            ProjectId = projectId,
            TaskId    = task.Id,
            EventType = EventTypes.TaskCreated,
            Source    = user.Email,
            Severity  = EventSeverity.Info,
            Data      = JsonSerializer.Serialize(new { task.Id, task.Objective, Type = task.Type.ToString(), TaskSource = task.Source.ToString() })
        }, ct);

        await repo.AddAsync(task, ct);

        var response = new Response(
            task.Id, task.ProjectId, task.Type, task.Status, task.Objective, task.Source, task.CreatedAt);

        if (idempotencyKey is not null)
            await idempotency.CacheResponseAsync(user.Id, idempotencyKey, response, ct);

        return Results.Created($"/api/v1/projects/{projectId}/tasks/{task.Id}", response);
    }
}
