using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Interfaces.Services;
using Microsoft.AspNetCore.Mvc;
using Pgvector;

namespace AgenticWorkforce.Api.Features.Documents;

public static class SearchDocuments
{
    public record Request(string Query, int Limit = 10);
    public record Match(Guid ChunkId, Guid DocumentId, int ChunkIndex, string Content,
        int? PageNumber, string? SectionTitle, double Score);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapPost("/api/v1/projects/{projectId:guid}/documents/search", HandleAsync)
            .RequireAuthorization(Policies.RequireViewer)
            .WithTags("Documents");

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        [FromBody] Request request,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        IEmbeddingService embeddings,
        IDocumentRepository repo,
        CancellationToken ct)
    {
        await authz.EnsureRoleAsync(userAccessor.User.Id, projectId, ProjectRole.Viewer, ct);

        if (!embeddings.IsConfigured)
            return Results.Problem(
                statusCode: 503,
                title: "Semantic search not yet available.",
                detail: "Embedding provider is wired in Phase 6.",
                extensions: new Dictionary<string, object?> { ["code"] = "EMBEDDING_NOT_CONFIGURED" });

        if (string.IsNullOrWhiteSpace(request.Query))
            throw new ValidationException("Query is required.");

        var limit = Math.Clamp(request.Limit, 1, 50);
        var queryVec = new Vector(await embeddings.EmbedAsync(request.Query, ct));
        var matches = await repo.SearchChunksAsync(projectId, queryVec, limit, ct);

        return Results.Ok(matches.Select(m => new Match(
            m.Chunk.Id, m.Chunk.DocumentId, m.Chunk.ChunkIndex, m.Chunk.Content,
            m.Chunk.PageNumber, m.Chunk.SectionTitle, m.Score)).ToList());
    }
}
