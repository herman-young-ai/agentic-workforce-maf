using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AgenticWorkforce.Infrastructure.Services;

/// <summary>
/// Inserts a zero-priced <see cref="ModelPricing"/> row for the stub model
/// family used by <see cref="AgenticWorkforce.Agents.Runtime.StubChatClient"/>.
/// Registered only when <c>AgentRuntime:DefaultProvider</c> is <c>stub</c>;
/// real providers never pay the cost of this seeder.
///
/// <para><b>Why this exists</b></para>
/// <c>ModelPricingService.CalculateCostAsync</c> fails fast when no
/// <c>ModelPricing</c> row matches (Principle 8 — no silent fallback to a
/// default rate). The stub provider has zero real cost; without a seeded
/// row the entire stub pipeline would throw on the first call. Seeding the
/// row at startup keeps the no-fallback invariant intact for real models
/// while letting the stub provider exercise the full pipeline in dev.
/// </summary>
internal sealed class StubModelPricingSeeder(
    IServiceScopeFactory scopes,
    ILogger<StubModelPricingSeeder> logger) : IHostedService
{
    public const string StubModelId = "stub-model";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var exists = await db.ModelPricings
            .AsNoTracking()
            .AnyAsync(p => p.Model == StubModelId, cancellationToken)
            .ConfigureAwait(false);

        if (exists)
        {
            LogAlreadySeeded(logger, StubModelId, null);
            return;
        }

        db.ModelPricings.Add(new ModelPricing
        {
            Model                    = StubModelId,
            EffectiveFrom            = DateTime.UtcNow,
            EffectiveTo              = null,
            CreatedAt                = DateTime.UtcNow,
            PricePerMtokInput        = 0m,
            PricePerMtokOutput       = 0m,
            PricePerMtokCacheRead    = 0m,
            PricePerMtokCacheCreate  = 0m
        });

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        LogSeeded(logger, StubModelId, null);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static readonly Action<ILogger, string, Exception?> LogSeeded =
        LoggerMessage.Define<string>(LogLevel.Information,
            new EventId(1, nameof(LogSeeded)),
            "Seeded zero-cost ModelPricing row for stub model {Model}.");

    private static readonly Action<ILogger, string, Exception?> LogAlreadySeeded =
        LoggerMessage.Define<string>(LogLevel.Debug,
            new EventId(2, nameof(LogAlreadySeeded)),
            "ModelPricing row for stub model {Model} already present; skipping seed.");
}
