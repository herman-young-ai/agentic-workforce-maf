using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;

namespace AgenticWorkforce.Api.Features.Admin.Catalog;

public static class AdminDisableAgent
{
    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapPost("/api/v1/admin/catalog/{agentId:guid}/disable", HandleAsync)
            .RequireAuthorization(Policies.RequirePlatformAdmin)
            .WithTags("AdminCatalog");

    private static async Task<IResult> HandleAsync(
        Guid agentId,
        IAgentCatalogRepository repo,
        CancellationToken ct)
    {
        if (!await repo.SetEnabledAsync(agentId, enabled: false, ct))
            throw new NotFoundException("AgentCatalog", agentId);
        return Results.NoContent();
    }
}
