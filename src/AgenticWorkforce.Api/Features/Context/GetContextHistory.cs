using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Interfaces.Services;

namespace AgenticWorkforce.Api.Features.Context;

public static class GetContextHistory
{
    public record Response(
        Guid Id,
        int ContextVersion,
        ChangeType ChangeType,
        string Path,
        string? OldValue,
        string? NewValue,
        string? AgentName,
        Guid? TaskId,
        string Reason,
        DateTime CreatedAt);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/projects/{projectId:guid}/context/history", HandleAsync)
            .RequireAuthorization(Policies.RequireViewer)
            .WithTags("Context");

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        IProjectContextService svc,
        CancellationToken ct)
    {
        await authz.EnsureRoleAsync(userAccessor.User.Id, projectId, ProjectRole.Viewer, ct);

        var history = await svc.GetHistoryAsync(projectId, ct);
        return Results.Ok(history.Select(c => new Response(
            c.Id, c.ContextVersion, c.ChangeType, c.Path, c.OldValue, c.NewValue,
            c.AgentName, c.TaskId, c.Reason, c.CreatedAt)).ToList());
    }
}
