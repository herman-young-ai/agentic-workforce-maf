using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Interfaces.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgenticWorkforce.Api.Features.Learnings;

public static class SearchLearnings
{
    public record Request(string Query, int Limit = 10);
    public record Match(Guid Id, LearningKind Kind, string Title, decimal Confidence, double Score);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapPost("/api/v1/projects/{projectId:guid}/learnings/search", HandleAsync)
            .RequireAuthorization(Policies.RequireViewer)
            .WithTags("Learnings");

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        [FromBody] Request request,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        IEmbeddingService embeddings,
        ILearningRepository repo,
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
        var queryEmbedding = await embeddings.EmbedAsync(request.Query, ct);
        var matches = await repo.SearchByEmbeddingAsync(projectId, queryEmbedding, limit, ct);

        return Results.Ok(matches
            .Select(m => new Match(m.Learning.Id, m.Learning.Kind, m.Learning.Title,
                m.Learning.Confidence, m.Score))
            .ToList());
    }
}
