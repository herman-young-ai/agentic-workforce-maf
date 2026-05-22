using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;

namespace AgenticWorkforce.Api.Features.Artifacts;

public static class GetArtifact
{
    public record Response(
        Guid Id,
        Guid TaskId,
        string? AgentName,
        ArtifactType ArtifactType,
        string Title,
        ContentFormat ContentFormat,
        long? FileSizeBytes,
        string? ContentHash,
        string? Language,
        DateTime? RetractedAt,
        string? RetractedBy,
        DateTime CreatedAt);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/projects/{projectId:guid}/artifacts/{artifactId:guid}", HandleAsync)
            .RequireAuthorization(Policies.RequireViewer)
            .WithTags("Artifacts");

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        Guid artifactId,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        IArtifactRepository repo,
        CancellationToken ct)
    {
        await authz.EnsureRoleAsync(userAccessor.User.Id, projectId, ProjectRole.Viewer, ct);

        var a = await repo.GetByIdAsync(artifactId, ct)
            ?? throw new NotFoundException("Artifact", artifactId);
        if (a.ProjectId != projectId)
            throw new NotFoundException("Artifact", artifactId);

        return Results.Ok(new Response(
            a.Id, a.TaskId, a.AgentName, a.ArtifactType, a.Title, a.ContentFormat,
            a.FileSizeBytes, a.ContentHash, a.Language, a.RetractedAt, a.RetractedBy, a.CreatedAt));
    }
}
