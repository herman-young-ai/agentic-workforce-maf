using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;

namespace AgenticWorkforce.Domain.Services;

/// <summary>
/// Single source of truth for the "can this principal use this project at
/// this role?" predicate. Pure function — takes the three inputs the rule
/// actually depends on (platform-admin bypass flag, the member row, the
/// required role) and returns a bool. Stays free of HttpContext, hub
/// context, or any ambient state so every caller can reuse it.
/// <para>
/// Before this existed, the predicate was duplicated in
/// <c>ProjectAuthorizationService.HasRoleAsync</c> (which reads the current
/// user from <c>ICurrentUserAccessor</c>) and inside <c>ProjectHub</c>
/// (which can't use the accessor because there's no HttpContext in a hub
/// method). Two copies of one rule will drift; centralising the predicate
/// makes drift impossible.
/// </para>
/// </summary>
public static class ProjectMembershipPolicy
{
    /// <summary>
    /// True when the principal should be allowed through a gate that
    /// requires at least <paramref name="minimumRole"/> on the project.
    /// PlatformAdmin bypasses the membership check entirely (it's a
    /// platform-level role; every project is reachable). Otherwise the
    /// member row must exist and have a role at or above the threshold.
    /// </summary>
    /// <param name="isPlatformAdmin">
    /// Whether the principal carries the <c>PlatformAdmin</c> role on its
    /// token. Resolved by the caller (HTTP handler reads from claims,
    /// SignalR hub reads from <c>Context.User</c>).
    /// </param>
    /// <param name="member">
    /// The principal's <c>ProjectMember</c> row for this project, or null
    /// when no membership exists.
    /// </param>
    /// <param name="minimumRole">The role threshold the gate requires.</param>
    public static bool IsAllowed(
        bool isPlatformAdmin,
        ProjectMember? member,
        ProjectRole minimumRole)
        => isPlatformAdmin || (member is not null && member.Role >= minimumRole);
}
