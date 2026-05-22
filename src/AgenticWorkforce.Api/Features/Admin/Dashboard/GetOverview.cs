using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Interfaces.Repositories;

namespace AgenticWorkforce.Api.Features.Admin.Dashboard;

public static class GetOverview
{
    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/admin/dashboard/overview", HandleAsync)
            .RequireAuthorization(Policies.RequirePlatformAdmin)
            .WithTags("AdminDashboard");

    private static async Task<IResult> HandleAsync(
        IPlatformStatsRepository stats,
        CancellationToken ct)
    {
        var overview = await stats.GetOverviewAsync(ct);
        return Results.Ok(overview);
    }
}
