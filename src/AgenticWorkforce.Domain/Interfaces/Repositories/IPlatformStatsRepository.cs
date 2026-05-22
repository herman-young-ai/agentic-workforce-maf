using AgenticWorkforce.Domain.Queries;

namespace AgenticWorkforce.Domain.Interfaces.Repositories;

/// <summary>
/// Cross-project aggregations for platform admin dashboards. Always
/// cross-tenant by definition — there is no projectId parameter. Callers
/// must be authorised at the PlatformAdmin policy level before invoking.
/// </summary>
public interface IPlatformStatsRepository
{
    Task<PlatformOverview> GetOverviewAsync(CancellationToken ct = default);
}
