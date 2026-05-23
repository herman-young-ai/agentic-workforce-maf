using AgenticWorkforce.Domain.Entities;

namespace AgenticWorkforce.Domain.Interfaces.Services;

/// <summary>
/// Publishes project events to subscribers (UI streams, audit pipelines).
///
/// <para><b>Transactional outbox semantics</b></para>
/// <see cref="PublishAsync(ProjectEvent, CancellationToken)"/> only ADDS
/// the event to the active DbContext. The caller's existing
/// <c>SaveChanges</c> (typically inside a repository mutation) commits
/// the row in the same transaction as the business change; an EF Core
/// interceptor then fans the event out to live transports after the
/// commit succeeds. This avoids the two-transaction failure mode where
/// the business mutation persists but the audit row is lost.
///
/// <para><b>Caller contract</b></para>
/// You MUST call <c>SaveChanges</c>/<c>SaveChangesAsync</c> on the same
/// scoped DbContext after invoking this overload (the order within the
/// unit of work doesn't matter — the interceptor sees the Added entry
/// at save time). Endpoint handlers get this for free via their
/// repository calls. Direct callers (background workers, tests
/// operating outside a repository) MUST explicitly call SaveChanges
/// or the event is silently lost.
/// </summary>
public interface IEventPublisher
{
    /// <summary>
    /// Stages a durable project event for persistence + live dispatch.
    /// See type docs for the transactional outbox contract.
    /// </summary>
    Task PublishAsync(ProjectEvent evt, CancellationToken ct = default);

    /// <summary>
    /// Publishes a transient signal (no DB counterpart) directly to the
    /// supplied pub/sub channel. Best-effort: failures are logged, not
    /// thrown.
    /// </summary>
    Task PublishAsync(
        string channel,
        string eventType,
        object data,
        CancellationToken ct = default);
}
