using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Pagination;

namespace AgenticWorkforce.Api.Features.Documents;

public static class ListDocuments
{
    public record Response(
        Guid Id,
        string FileName,
        string ContentType,
        long FileSizeBytes,
        DocumentType DocumentType,
        ExtractionStatus ExtractionStatus,
        DateTime CreatedAt);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/projects/{projectId:guid}/documents", HandleAsync)
            .RequireAuthorization(Policies.RequireViewer)
            .WithTags("Documents");

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        [AsParameters] PagedQuery paging,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        IDocumentRepository repo,
        CancellationToken ct)
    {
        await authz.EnsureRoleAsync(userAccessor.User.Id, projectId, ProjectRole.Viewer, ct);

        var page = await repo.ListByProjectPagedAsync(projectId, paging, ct);
        var items = page.Items
            .Select(d => new Response(d.Id, d.FileName, d.ContentType, d.FileSizeBytes,
                d.DocumentType, d.ExtractionStatus, d.CreatedAt))
            .ToList();
        return Results.Ok(new PagedResult<Response>(items, page.Page, page.PageSize, page.TotalCount));
    }
}
