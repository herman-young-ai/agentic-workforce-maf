using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Interfaces.Repositories;

namespace AgenticWorkforce.Api.Features.Admin.Catalog;

public static class AdminPromptHistory
{
    private const string EntityTypeAgentCatalog = "AgentCatalog";

    public record Response(
        Guid Id,
        int Version,
        string PromptType,
        string Content,
        string? ChangedBy,
        string? ChangeReason,
        DateTime CreatedAt);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/admin/catalog/{agentId:guid}/prompt-history", HandleAsync)
            .RequireAuthorization(Policies.RequirePlatformAdmin)
            .WithTags("AdminCatalog");

    private static async Task<IResult> HandleAsync(
        Guid agentId,
        IPromptVersionRepository repo,
        CancellationToken ct)
    {
        var history = await repo.ListByEntityAsync(EntityTypeAgentCatalog, agentId, ct);
        return Results.Ok(history.Select(p => new Response(
            p.Id, p.Version, p.PromptType, p.Content, p.ChangedBy, p.ChangeReason, p.CreatedAt)).ToList());
    }
}
