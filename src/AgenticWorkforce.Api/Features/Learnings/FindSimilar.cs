using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Interfaces.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgenticWorkforce.Api.Features.Learnings;

public static class FindSimilar
{
    public record Match(Guid Id, LearningKind Kind, string Title, decimal Confidence, double Score);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/projects/{projectId:guid}/learnings/{learningId:guid}/similar", HandleAsync)
            .RequireAuthorization(Policies.RequireViewer)
            .WithTags("Learnings");

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        Guid learningId,
        [FromQuery] int? limit,
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

        var seed = await repo.GetByIdAsync(learningId, ct)
            ?? throw new NotFoundException("Learning", learningId);
        if (seed.ProjectId != projectId)
            throw new NotFoundException("Learning", learningId);

        var k = Math.Clamp(limit ?? 5, 1, 50);
        var matches = await repo.FindSimilarAsync(learningId, k, ct);

        return Results.Ok(matches
            .Select(m => new Match(m.Learning.Id, m.Learning.Kind, m.Learning.Title,
                m.Learning.Confidence, m.Score))
            .ToList());
    }
}
