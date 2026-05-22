using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Interfaces.Repositories;

namespace AgenticWorkforce.Api.Features.Team;

public static class ListTeam
{
    public record Response(
        Guid Id,
        Guid AgentCatalogId,
        string AgentName,
        AgentRole Role,
        string? UserPrompt,
        bool Enabled,
        int DisplayOrder);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/projects/{projectId:guid}/team", HandleAsync)
            .RequireAuthorization(Policies.RequireViewer)
            .WithTags("Team")
            ;

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        IProjectAgentRepository projectAgents,
        CancellationToken ct)
    {
        var user = userAccessor.User;
        await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Viewer, ct);

        var team = await projectAgents.ListByProjectAsync(projectId, ct);

        var response = team
            .Select(a => new Response(a.Id, a.AgentCatalogId, a.AgentCatalog.AgentName, a.Role,
                a.UserPrompt, a.Enabled, a.DisplayOrder))
            .ToList();

        return Results.Ok(response);
    }
}
