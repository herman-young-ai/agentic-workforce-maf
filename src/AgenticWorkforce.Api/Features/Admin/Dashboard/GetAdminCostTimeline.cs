using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Interfaces.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgenticWorkforce.Api.Features.Admin.Dashboard;

public static class GetAdminCostTimeline
{
    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/admin/dashboard/costs/timeline", HandleAsync)
            .RequireAuthorization(Policies.RequirePlatformAdmin)
            .WithTags("AdminDashboard");

    private static async Task<IResult> HandleAsync(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        ICostQueryService svc,
        CancellationToken ct)
    {
        var timeline = await svc.GetTimelineAllProjectsAsync(from, to, ct);
        return Results.Ok(timeline);
    }
}
