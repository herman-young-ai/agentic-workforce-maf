using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Infrastructure.Data;
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
        AppDbContext db,
        CancellationToken ct)
    {
        var user = userAccessor.User;
        await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Operator, ct);

        if (idempotencyKey is not null)
        {
            var cached = await idempotency.GetCachedResponseAsync<Response>(idempotencyKey, ct);
            if (cached is not null)
                return Results.Created($"/api/v1/projects/{projectId}/tasks/{cached.Id}", cached);
        }

        if (string.IsNullOrWhiteSpace(request.Objective))
            throw new ValidationException("Task objective is required.");

        var task = new AgenticTask
        {
            ProjectId    = projectId,
            Objective    = request.Objective,
            Type         = request.Type,
            Source       = request.Source,
            Status       = TaskStatus.Proposed,
            AgentName    = request.AgentName,
            Inputs       = request.Inputs,
            MaxRetries   = request.MaxRetries,
            ParentTaskId = request.ParentTaskId,
            CreatedById  = user.Id,
            FormatVersion = "1.0"
        };

        db.Tasks.Add(task);
        await db.SaveChangesAsync(ct);

        var response = new Response(task.Id, task.ProjectId, task.Type, task.Status, task.Objective, task.Source, task.CreatedAt);

        if (idempotencyKey is not null)
            await idempotency.CacheResponseAsync(idempotencyKey, response, ct);

        return Results.Created($"/api/v1/projects/{projectId}/tasks/{task.Id}", response);
    }
}
