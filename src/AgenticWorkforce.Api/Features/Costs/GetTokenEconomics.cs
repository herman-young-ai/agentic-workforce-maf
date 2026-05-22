using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Interfaces.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgenticWorkforce.Api.Features.Costs;

public static class GetTokenEconomics
{
    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/projects/{projectId:guid}/costs/token-economics", HandleAsync)
            .RequireAuthorization(Policies.RequireViewer)
            .WithTags("Costs");

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        ICostQueryService svc,
        CancellationToken ct)
    {
        await authz.EnsureRoleAsync(userAccessor.User.Id, projectId, ProjectRole.Viewer, ct);
        var econ = await svc.GetTokenEconomicsAsync(projectId, from, to, ct);
        return Results.Ok(econ);
    }
}
