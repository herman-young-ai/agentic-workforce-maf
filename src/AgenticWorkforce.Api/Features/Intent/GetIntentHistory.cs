using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Interfaces.Repositories;

namespace AgenticWorkforce.Api.Features.Intent;

public static class GetIntentHistory
{
    public record Response(
        Guid Id,
        string IntentText,
        string IntentSummary,
        IntentSource Source,
        string Reason,
        Guid? RevisedFromId,
        DateTime CreatedAt);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/projects/{projectId:guid}/intent/history", HandleAsync)
            .RequireAuthorization(Policies.RequireViewer)
            .WithTags("Intent");

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        IIntentRepository repo,
        CancellationToken ct)
    {
        await authz.EnsureRoleAsync(userAccessor.User.Id, projectId, ProjectRole.Viewer, ct);

        var history = await repo.GetHistoryAsync(projectId, ct);
        return Results.Ok(history.Select(i => new Response(
            i.Id, i.Intent, i.IntentSummary, i.Source, i.Reason, i.RevisedFromId, i.CreatedAt)).ToList());
    }
}
