using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;

namespace AgenticWorkforce.Api.Features.Artifacts;

public static class RetractArtifact
{
    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapPost("/api/v1/projects/{projectId:guid}/artifacts/{artifactId:guid}/retract", HandleAsync)
            .RequireAuthorization(Policies.RequireOwner)
            .WithTags("Artifacts");

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        Guid artifactId,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        IArtifactRepository repo,
        CancellationToken ct)
    {
        var user = userAccessor.User;
        await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Owner, ct);

        var a = await repo.GetByIdAsync(artifactId, ct)
            ?? throw new NotFoundException("Artifact", artifactId);
        if (a.ProjectId != projectId)
            throw new NotFoundException("Artifact", artifactId);

        await repo.RetractAsync(artifactId, user.Email, ct);
        return Results.NoContent();
    }
}
