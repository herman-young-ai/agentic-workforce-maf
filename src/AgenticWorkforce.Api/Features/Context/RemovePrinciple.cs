using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Services;

namespace AgenticWorkforce.Api.Features.Context;

public static class RemovePrinciple
{
    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapDelete("/api/v1/projects/{projectId:guid}/context/principles/{principleId}", HandleAsync)
            .RequireAuthorization(Policies.RequireOwner)
            .WithTags("Context");

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        string principleId,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        IProjectContextService svc,
        CancellationToken ct)
    {
        var user = userAccessor.User;
        await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Owner, ct);

        if (!await svc.RemovePrincipleAsync(projectId, principleId, user.Id, ct))
            throw new NotFoundException("Principle", principleId);

        return Results.NoContent();
    }
}
