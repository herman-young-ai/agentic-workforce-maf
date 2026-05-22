using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Pagination;

namespace AgenticWorkforce.Api.Features.Artifacts;

public static class ListArtifacts
{
    public record Response(
        Guid Id,
        Guid TaskId,
        ArtifactType ArtifactType,
        string Title,
        ContentFormat ContentFormat,
        long? FileSizeBytes,
        DateTime CreatedAt);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/projects/{projectId:guid}/artifacts", HandleAsync)
            .RequireAuthorization(Policies.RequireViewer)
            .WithTags("Artifacts");

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        [AsParameters] PagedQuery paging,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        IArtifactRepository repo,
        CancellationToken ct)
    {
        await authz.EnsureRoleAsync(userAccessor.User.Id, projectId, ProjectRole.Viewer, ct);

        var page = await repo.ListByProjectPagedAsync(projectId, paging, ct);
        var items = page.Items
            .Select(a => new Response(a.Id, a.TaskId, a.ArtifactType, a.Title, a.ContentFormat,
                a.FileSizeBytes, a.CreatedAt))
            .ToList();
        return Results.Ok(new PagedResult<Response>(items, page.Page, page.PageSize, page.TotalCount));
    }
}
