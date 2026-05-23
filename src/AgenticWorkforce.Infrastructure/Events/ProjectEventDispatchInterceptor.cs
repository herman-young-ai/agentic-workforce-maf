using System.Text.Json;
using AgenticWorkforce.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace AgenticWorkforce.Infrastructure.Events;

/// <summary>
/// EF Core save-changes interceptor that turns the publish call into a
/// transactional outbox.
///
/// <para><b>Why this exists</b></para>
/// Before this interceptor, <see cref="RedisEventPublisher"/> ran its own
/// <c>SaveChangesAsync</c>, which meant any endpoint flow like
/// <c>repo.UpdateAsync(...); publisher.PublishAsync(...);</c> committed in
/// TWO separate transactions. If the second commit failed (DB drop, lock
/// timeout, FK violation), the business mutation was already persisted
/// but no audit row was — a regulatory gap for a 7-year-retention audit
/// log.
///
/// <para><b>What this does</b></para>
/// The publisher now only <c>Adds</c> the <see cref="ProjectEvent"/> to
/// the active <c>DbContext</c>. Whatever <c>SaveChanges</c> the endpoint
/// performs (typically via its repository) flushes the event row in the
/// SAME transaction as the business change. Then this interceptor's
/// <see cref="SavedChangesAsync"/> hook runs — after the COMMIT — and
/// dispatches the rows to Redis pub/sub. If the dispatch fails, the DB
/// rows are already durably committed; clients reconcile via the events
/// feed on reconnect (best-effort live transport semantics, unchanged).
///
/// <para><b>Lifetime</b></para>
/// Registered <c>Scoped</c> in DI so a fresh instance exists per
/// <c>AppDbContext</c>. The <c>_pending</c> field is therefore safe to
/// use as per-request state — no AsyncLocal needed.
/// </summary>
internal sealed class ProjectEventDispatchInterceptor(
    IRedisPubSubService redisPubSub,
    ILogger<ProjectEventDispatchInterceptor> logger) : SaveChangesInterceptor
{
    private List<ProjectEvent>? _pending;

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
        {
            // Snapshot the freshly-Added rows BEFORE the save runs. After
            // a successful save the entries' State flips to Unchanged so
            // we can't tell which were just inserted.
            var added = eventData.Context.ChangeTracker.Entries<ProjectEvent>()
                .Where(e => e.State == EntityState.Added)
                .Select(e => e.Entity)
                .ToList();
            _pending = added.Count == 0 ? null : added;
        }
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        var pending = _pending;
        _pending = null;
        if (pending is null) return await base.SavedChangesAsync(eventData, result, cancellationToken);

        // SavedChangesAsync fires after each SaveChanges DB call returns,
        // which for an EXPLICIT transaction is BEFORE the user commits.
        // Dispatching to Redis here would publish events that a later
        // tx.Rollback() would un-persist — phantom events for clients.
        // Fail fast rather than risk silent inconsistency. The right
        // solution when explicit transactions become necessary is to hook
        // tx.Committed; until then, no caller in the codebase uses them.
        if (eventData.Context?.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException(
                "ProjectEventDispatchInterceptor cannot dispatch events while a manual "
                + "DbContext transaction is open — the interceptor fires before the "
                + "transaction commits, so a later rollback would orphan the published "
                + "Redis messages. Either publish events outside the BeginTransaction "
                + "scope, or wire transaction-commit hooks to call dispatch explicitly.");
        }

        // Dispatch sequentially. The volume is low (one or two events per
        // endpoint call typically) and a parallel fan-out would complicate
        // error attribution without buying anything material.
        foreach (var evt in pending)
        {
            try
            {
                var payload = JsonSerializer.Serialize(
                    ProjectEventDto.From(evt), WireJsonOptions.Default);
                await redisPubSub.PublishAsync(
                    RedisChannels.ProjectEvents(evt.ProjectId),
                    payload,
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // DB row is committed and authoritative — clients replay
                // via the events feed on reconnect.
                logger.LogWarning(ex,
                    "Redis pub/sub failed for {EventType} on project {ProjectId}; "
                    + "event persisted, clients will pick it up via the events feed",
                    evt.EventType, evt.ProjectId);
            }

            logger.LogInformation(
                "Dispatched {EventType} for project {ProjectId}",
                evt.EventType, evt.ProjectId);
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        // The transaction rolled back; drop any pending dispatches so
        // we don't fan out events that never persisted.
        _pending = null;
        base.SaveChangesFailed(eventData);
    }

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        _pending = null;
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }
}
