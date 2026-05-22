using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;

namespace AgenticWorkforce.Api.Features.Team;

public static class RemoveAgent
{
    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapDelete("/api/v1/projects/{projectId:guid}/team/{memberId:guid}", HandleAsync)
            .RequireAuthorization(Policies.RequireOwner)
            .WithTags("Team")
            ;

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        Guid memberId,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        IProjectAgentRepository projectAgents,
        CancellationToken ct)
    {
        var user = userAccessor.User;
        await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Owner, ct);

        var agent = await projectAgents.GetByIdAsync(memberId, ct);
        if (agent is null || agent.ProjectId != projectId)
            throw new NotFoundException("ProjectAgent", memberId);

        if (!await projectAgents.RemoveAsync(memberId, ct))
            throw new NotFoundException("ProjectAgent", memberId);

        return Results.NoContent();
    }
}
