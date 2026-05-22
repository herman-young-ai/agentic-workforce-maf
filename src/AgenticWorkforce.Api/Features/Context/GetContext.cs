using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Interfaces.Services;

namespace AgenticWorkforce.Api.Features.Context;

public static class GetContext
{
    public record Response(
        Guid Id,
        Guid ProjectId,
        string ContextData,
        int ContextVersion,
        int SizeCharacters,
        int SizeTokens,
        string FormatVersion);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/projects/{projectId:guid}/context", HandleAsync)
            .RequireAuthorization(Policies.RequireViewer)
            .WithTags("Context");

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        IProjectContextService svc,
        CancellationToken ct)
    {
        await authz.EnsureRoleAsync(userAccessor.User.Id, projectId, ProjectRole.Viewer, ct);

        var c = await svc.GetAsync(projectId, ct);
        return Results.Ok(new Response(
            c.Id, c.ProjectId, c.ContextData, c.ContextVersion,
            c.SizeCharacters, c.SizeTokens, c.FormatVersion));
    }
}
