using AgenticWorkforce.Api.Core.Auth;

namespace AgenticWorkforce.Api.Features.Admin.Dashboard;

public static class GetHealth
{
    public record DependencyStatus(string Name, string Status, string? Detail);
    public record Response(string Status, IReadOnlyList<DependencyStatus> Dependencies);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/admin/dashboard/health", HandleAsync)
            .RequireAuthorization(Policies.RequirePlatformAdmin)
            .WithTags("AdminDashboard");

    private static Task<IResult> HandleAsync(CancellationToken ct)
    {
        // Detailed cross-component health lands with Phase 5/11; for Phase 4
        // this endpoint exists for the auth surface and returns a baseline
        // shape that platform admins can consume.
        var dependencies = new List<DependencyStatus>
        {
            new("database", "Healthy", null),
            new("redis", "Pending", "Wired in Phase 5"),
            new("embeddings", "Pending", "Wired in Phase 6")
        };
        return Task.FromResult(Results.Ok(new Response("Healthy", dependencies)));
    }
}
