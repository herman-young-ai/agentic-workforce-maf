using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Queries;

namespace AgenticWorkforce.Api.Features.Artifacts;

public static class GetArtifactContent
{
    public record Response(string? InlineText, string? StorageUrl, string ContentFormat);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/projects/{projectId:guid}/artifacts/{artifactId:guid}/content", HandleAsync)
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

        var meta = await repo.GetByIdAsync(artifactId, ct)
            ?? throw new NotFoundException("Artifact", artifactId);
        if (meta.ProjectId != projectId)
            throw new NotFoundException("Artifact", artifactId);

        var content = await repo.GetContentAsync(artifactId, ct)
            ?? throw new NotFoundException("Artifact", artifactId);

        return Results.Ok(new Response(content.InlineText, content.StorageUrl, content.ContentFormat));
    }
}
