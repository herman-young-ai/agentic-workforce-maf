using AgenticWorkforce.Infrastructure.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AgenticWorkforce.Api.Core.Health;

/// <summary>
/// PostgreSQL connectivity health check. <see cref="DatabaseFacade.CanConnectAsync"/>
/// already traps connection failures and returns <c>false</c> — no try/catch needed.
/// </summary>
public class DatabaseHealthCheck(AppDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
        => await db.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy("PostgreSQL connection is healthy.")
            : HealthCheckResult.Unhealthy("PostgreSQL connection failed.");
}
