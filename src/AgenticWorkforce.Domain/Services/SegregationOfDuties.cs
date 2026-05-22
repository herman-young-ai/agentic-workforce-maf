using AgenticWorkforce.Domain.Exceptions;

namespace AgenticWorkforce.Domain.Services;

/// <summary>
/// Single source of truth for Principle 11: the person who originates an
/// action cannot also approve, complete, or otherwise close it. Used by task
/// approval, bulk approval, and knowledge-promotion gates.
/// <para>
/// Two API shapes:
/// <list type="bullet">
///   <item>
///     <see cref="Enforce"/> — throws <see cref="ForbiddenException"/>. Use
///     in single-item endpoint handlers where SoD failure must surface as a
///     403.
///   </item>
///   <item>
///     <see cref="IsAllowed"/> — boolean predicate. Use in batch operations
///     that report per-item outcomes (e.g. <c>BulkApprove</c>) where a
///     blocked item is one row in the result rather than a request failure.
///   </item>
/// </list>
/// </para>
/// </summary>
public static class SegregationOfDuties
{
    /// <summary>
    /// Returns false when the actor is the same identity that originated the
    /// action — meaning Principle 11 forbids them from completing it.
    /// A null originator means there is no known originator on record (e.g.
    /// system-created task), so SoD does not apply.
    /// </summary>
    public static bool IsAllowed(Guid? originatorId, Guid actorId)
        => !(originatorId.HasValue && originatorId.Value == actorId);

    /// <summary>
    /// Throws <see cref="ForbiddenException"/> when SoD would forbid the
    /// actor from performing <paramref name="action"/>. <paramref name="action"/>
    /// is interpolated into the error message and must read naturally after
    /// "the originator cannot" (e.g. <c>"approve their own task"</c>,
    /// <c>"approve their own promotion request"</c>).
    /// </summary>
    public static void Enforce(Guid? originatorId, Guid actorId, string action)
    {
        if (!IsAllowed(originatorId, actorId))
            throw new ForbiddenException(
                $"Segregation of duties: the originator cannot {action}.");
    }
}
