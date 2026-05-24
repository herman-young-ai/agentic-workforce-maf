using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Interfaces.Services;

namespace AgenticWorkforce.Infrastructure.Services;

internal sealed class ModelPricingService(
    IModelPricingRepository repo,
    TimeProvider clock) : IModelPricingService
{
    private const decimal MillionTokens = 1_000_000m;

    public async Task<decimal> EstimateInputCostAsync(string model, int inputTokens, CancellationToken ct = default)
    {
        var pricing = await ResolveAsync(model, ct);
        return (decimal)inputTokens / MillionTokens * pricing.PricePerMtokInput;
    }

    public async Task<decimal> CalculateCostAsync(
        string model,
        long input,
        long output,
        long cacheRead,
        long cacheCreate,
        CancellationToken ct = default)
    {
        var p = await ResolveAsync(model, ct);
        return ((decimal)input        / MillionTokens * p.PricePerMtokInput)
             + ((decimal)output       / MillionTokens * p.PricePerMtokOutput)
             + ((decimal)cacheRead    / MillionTokens * p.PricePerMtokCacheRead)
             + ((decimal)cacheCreate  / MillionTokens * p.PricePerMtokCacheCreate);
    }

    private async Task<Domain.Entities.ModelPricing> ResolveAsync(string model, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var pricing = await repo.GetEffectiveAsync(model, now, ct);
        if (pricing is null)
            throw new InvalidStateException(
                $"No ModelPricing row found for model '{model}' effective at {now:O}. Pricing must be configured before agent execution (no silent fallback to a default rate).");
        return pricing;
    }
}
