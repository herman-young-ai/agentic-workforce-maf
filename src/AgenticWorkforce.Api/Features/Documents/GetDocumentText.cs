using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;

namespace AgenticWorkforce.Api.Features.Documents;

public static class GetDocumentText
{
    public record Response(string? ExtractedText, string? ExtractedTextUrl, ExtractionStatus Status);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/projects/{projectId:guid}/documents/{documentId:guid}/text", HandleAsync)
            .RequireAuthorization(Policies.RequireViewer)
            .WithTags("Documents");

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        Guid documentId,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        IDocumentRepository repo,
        CancellationToken ct)
    {
        await authz.EnsureRoleAsync(userAccessor.User.Id, projectId, ProjectRole.Viewer, ct);

        var d = await repo.GetByIdAsync(documentId, ct)
            ?? throw new NotFoundException("Document", documentId);
        if (d.ProjectId != projectId)
            throw new NotFoundException("Document", documentId);

        return Results.Ok(new Response(d.ExtractedText, d.ExtractedTextUrl, d.ExtractionStatus));
    }
}
