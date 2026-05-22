using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Interfaces.Repositories;

namespace AgenticWorkforce.Api.Features.Admin.Catalog;

public static class AdminListCatalog
{
    public record Response(
        Guid Id,
        string AgentName,
        string? AgentType,
        string? AgentVersion,
        bool Enabled,
        bool ChatEnabled,
        AgentVisibility Visibility);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/admin/catalog", HandleAsync)
            .RequireAuthorization(Policies.RequirePlatformAdmin)
            .WithTags("AdminCatalog");

    private static async Task<IResult> HandleAsync(
        IAgentCatalogRepository repo,
        CancellationToken ct)
    {
        var list = await repo.ListAllAsync(ct);
        return Results.Ok(list.Select(a => new Response(
            a.Id, a.AgentName, a.AgentType, a.AgentVersion, a.Enabled, a.ChatEnabled, a.Visibility)).ToList());
    }
}
