namespace AgenticWorkforce.Domain.Interfaces.Services;

/// <summary>
/// Resolves model pricing and computes call costs from token counts. Backed
/// by <see cref="Repositories.IModelPricingRepository"/> which queries the
/// <c>ModelPricing</c> table by (model, EffectiveFrom &lt;= now &lt; EffectiveTo).
/// Carries all four token classes because <c>ModelPricing</c> has separate
/// cache-read and cache-create rates — ignoring them would under- or
/// over-charge Claude calls. Fails fast (no fallback to a hardcoded rate)
/// when no row matches.
/// </summary>
public interface IModelPricingService
{
    Task<decimal> EstimateInputCostAsync(string model, int inputTokens, CancellationToken ct = default);

    Task<decimal> CalculateCostAsync(
        string model,
        long input,
        long output,
        long cacheRead,
        long cacheCreate,
        CancellationToken ct = default);
}
