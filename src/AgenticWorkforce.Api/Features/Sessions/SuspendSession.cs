using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;

namespace AgenticWorkforce.Api.Features.Sessions;

public static class SuspendSession
{
    public record Request(string Reason);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapPost("/api/v1/projects/{projectId:guid}/sessions/{sessionId:guid}/suspend", HandleAsync)
            .RequireAuthorization(Policies.RequireOperator)
            .WithTags("Sessions")
            ;

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        Guid sessionId,
        [FromBody] Request request,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        ISessionRepository repo,
        AppDbContext db,
        CancellationToken ct)
    {
        var user = userAccessor.User;
        await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Operator, ct);

        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new ValidationException("Suspension reason is required.");

        var session = await repo.GetByIdAsync(sessionId, ct)
            ?? throw new NotFoundException("Session", sessionId);

        if (session.ProjectId != projectId)
            throw new NotFoundException("Session", sessionId);

        if (session.Status != SessionStatus.Active)
            throw new InvalidStateException($"Only active sessions can be suspended (current status: {session.Status}).");

        session.Status = SessionStatus.Suspended;
        session.RollingSummary = string.IsNullOrEmpty(session.RollingSummary)
            ? $"Suspended: {request.Reason}"
            : $"{session.RollingSummary}\nSuspended: {request.Reason}";

        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }
}
