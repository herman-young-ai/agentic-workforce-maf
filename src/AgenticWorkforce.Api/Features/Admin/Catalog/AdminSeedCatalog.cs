using AgenticWorkforce.Api.Core.Auth;

namespace AgenticWorkforce.Api.Features.Admin.Catalog;

public static class AdminSeedCatalog
{
    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapPost("/api/v1/admin/catalog/seed", HandleAsync)
            .RequireAuthorization(Policies.RequirePlatformAdmin)
            .WithTags("AdminCatalog");

    private static Task<IResult> HandleAsync(CancellationToken ct)
    {
        // Phase 4 plan §4.17: stub that returns 501 until Phase 7 produces the
        // canonical YAML catalog. The endpoint exists so admin tooling and
        // tests can target it now; the implementation moves with Phase 7.
        return Task.FromResult(Results.Problem(
            statusCode: 501,
            title: "Catalog seeding not implemented.",
            detail: "Catalog seeding from YAML lands in Phase 7.",
            extensions: new Dictionary<string, object?> { ["code"] = "CATALOG_SEED_NOT_READY" }));
    }
}
