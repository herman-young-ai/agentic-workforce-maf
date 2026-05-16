using AgenticWorkforce.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AgenticWorkforce.Infrastructure.Data;

/// <summary>
/// Stamps UpdatedAt on modified entities. CRUD-level audit (who changed what)
/// is a separate concern from the 3-layer compliance audit pipeline (ADR-008).
/// Adopted from SecurityBff reference architecture.
/// </summary>
public sealed class AuditInterceptor : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is null)
            return base.SavingChangesAsync(eventData, result, cancellationToken);

        var now = DateTime.UtcNow;

        foreach (var entry in eventData.Context.ChangeTracker.Entries<EntityBase>()
                     .Where(e => e.State == EntityState.Modified))
        {
            entry.Entity.UpdatedAt = now;
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
