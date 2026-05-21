using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;

namespace AgenticWorkforce.Api.Features.Sessions;

public static class CreateSession
{
    public record Request(
        string? Goal,
        string? AgentName,
        decimal? CostBudgetUsd,
        DateTime? ExpiresAt);

    public record Response(
        Guid Id,
        Guid ProjectId,
        SessionStatus Status,
        string? Goal,
        string? AgentName,
        DateTime CreatedAt);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapPost("/api/v1/projects/{projectId:guid}/sessions", HandleAsync)
            .RequireAuthorization(Policies.RequireOperator)
            .WithTags("Sessions")
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
                return Results.Created($"/api/v1/projects/{projectId}/sessions/{cached.Id}", cached);
        }

        var session = new Session
        {
            ProjectId    = projectId,
            UserId       = user.Id,
            Goal         = request.Goal,
            AgentName    = request.AgentName,
            CostBudgetUsd = request.CostBudgetUsd,
            ExpiresAt    = request.ExpiresAt
        };

        db.Sessions.Add(session);
        await db.SaveChangesAsync(ct);

        var response = new Response(session.Id, session.ProjectId, session.Status, session.Goal, session.AgentName, session.CreatedAt);

        if (idempotencyKey is not null)
            await idempotency.CacheResponseAsync(idempotencyKey, response, ct);

        return Results.Created($"/api/v1/projects/{projectId}/sessions/{session.Id}", response);
    }
}
