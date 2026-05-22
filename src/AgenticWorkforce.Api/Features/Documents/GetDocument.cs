using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;

namespace AgenticWorkforce.Api.Features.Documents;

public static class GetDocument
{
    public record Response(
        Guid Id,
        string FileName,
        string ContentType,
        long FileSizeBytes,
        DocumentType DocumentType,
        string? Description,
        IReadOnlyList<string> Tags,
        ExtractionStatus ExtractionStatus,
        string? ExtractionError,
        int? PageCount,
        bool EmbeddingsGenerated,
        int ChunkCount,
        DateTime? RetractedAt,
        DateTime CreatedAt);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/projects/{projectId:guid}/documents/{documentId:guid}", HandleAsync)
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

        return Results.Ok(new Response(
            d.Id, d.FileName, d.ContentType, d.FileSizeBytes, d.DocumentType,
            d.Description, d.Tags, d.ExtractionStatus, d.ExtractionError, d.PageCount,
            d.EmbeddingsGenerated, d.ChunkCount, d.RetractedAt, d.CreatedAt));
    }
}
