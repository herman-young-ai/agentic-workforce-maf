using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;

namespace AgenticWorkforce.Api.Features.Admin.Catalog;

public static class AdminDeleteAgent
{
    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapDelete("/api/v1/admin/catalog/{agentId:guid}", HandleAsync)
            .RequireAuthorization(Policies.RequirePlatformAdmin)
            .WithTags("AdminCatalog");

    private static async Task<IResult> HandleAsync(
        Guid agentId,
        IAgentCatalogRepository repo,
        CancellationToken ct)
    {
        // Soft delete: disable rather than remove. ProjectAgents may reference
        // this catalog entry; permanent deletion happens out-of-band via Phase
        // 11 maintenance once no project depends on it.
        if (!await repo.SetEnabledAsync(agentId, enabled: false, ct))
            throw new NotFoundException("AgentCatalog", agentId);

        return Results.NoContent();
    }
}
