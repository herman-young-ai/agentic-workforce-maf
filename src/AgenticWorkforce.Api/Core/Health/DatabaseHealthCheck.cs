using AgenticWorkforce.Infrastructure.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AgenticWorkforce.Api.Core.Health;

/// <summary>
/// PostgreSQL connectivity health check.
/// Adopted from SecurityBff reference.
/// </summary>
public class DatabaseHealthCheck(AppDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await db.Database.CanConnectAsync(cancellationToken);
            return HealthCheckResult.Healthy("PostgreSQL connection is healthy.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL connection failed.", ex);
        }
    }
}
