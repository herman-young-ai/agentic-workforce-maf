using AgenticWorkforce.Domain.Entities;

namespace AgenticWorkforce.Domain.Interfaces.Repositories;

/// <summary>
/// Reads from the <c>ModelPricing</c> table. The pricing service composes this
/// repository with the system clock to resolve the current rate for a model.
/// </summary>
public interface IModelPricingRepository
{
    /// <summary>
    /// Returns the pricing row whose validity window <c>[EffectiveFrom, EffectiveTo)</c>
    /// contains <paramref name="atUtc"/>. Returns <c>null</c> if no row matches —
    /// callers should treat this as an unrecoverable configuration error
    /// (Principle 8: fail fast) rather than substituting a default rate.
    /// </summary>
    Task<ModelPricing?> GetEffectiveAsync(string model, DateTime atUtc, CancellationToken ct = default);
}
