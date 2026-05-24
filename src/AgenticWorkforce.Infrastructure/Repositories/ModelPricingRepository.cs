using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AgenticWorkforce.Infrastructure.Repositories;

internal sealed class ModelPricingRepository(AppDbContext db) : IModelPricingRepository
{
    public Task<ModelPricing?> GetEffectiveAsync(string model, DateTime atUtc, CancellationToken ct = default)
        => db.ModelPricings
            .AsNoTracking()
            .Where(p => p.Model == model
                     && p.EffectiveFrom <= atUtc
                     && (p.EffectiveTo == null || p.EffectiveTo > atUtc))
            .OrderByDescending(p => p.EffectiveFrom)
            .FirstOrDefaultAsync(ct);
}
