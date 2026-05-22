using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Interfaces.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgenticWorkforce.Api.Features.Admin.Dashboard;

public static class GetAdminCosts
{
    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/admin/dashboard/costs", HandleAsync)
            .RequireAuthorization(Policies.RequirePlatformAdmin)
            .WithTags("AdminDashboard");

    private static async Task<IResult> HandleAsync(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        ICostQueryService svc,
        CancellationToken ct)
    {
        var summary = await svc.GetSummaryAllProjectsAsync(from, to, ct);
        return Results.Ok(summary);
    }
}
