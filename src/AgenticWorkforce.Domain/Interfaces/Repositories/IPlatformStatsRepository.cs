namespace AgenticWorkforce.Domain.Interfaces.Repositories;

public record PlatformOverview(
    int TotalProjects,
    int ActiveProjects,
    int TotalUsers,
    int ActiveUsers,
    int AgentCatalogSize);

/// <summary>
/// Cross-project aggregations for platform admin dashboards. Always
/// cross-tenant by definition — there is no projectId parameter. Callers
/// must be authorised at the PlatformAdmin policy level before invoking.
/// </summary>
public interface IPlatformStatsRepository
{
    Task<PlatformOverview> GetOverviewAsync(CancellationToken ct = default);
}
