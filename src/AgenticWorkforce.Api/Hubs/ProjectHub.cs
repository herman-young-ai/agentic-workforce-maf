using System.Security.Claims;
using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace AgenticWorkforce.Api.Hubs;

/// <summary>
/// SignalR hub for live project + session updates. The hub exposes group
/// membership operations (join/leave) and is the target the
/// <c>SignalREventRelay</c> background service pushes events to.
///
/// <para><b>Security posture</b></para>
/// Every join method verifies project membership BEFORE adding the
/// connection to its SignalR group. Without the gate, any authenticated
/// client could subscribe to any project's events by guessing an id —
/// exactly the BOLA pattern Principle 14 forbids. Authorisation failures
/// bubble up as <see cref="HubException"/> so the client sees a
/// structured error, not a silent no-op join.
///
/// <para>
/// The hub talks to <see cref="IProjectMemberRepository"/> directly rather
/// than via <c>IProjectAuthorizationService</c> because the latter resolves
/// the current user from <c>IHttpContextAccessor</c>, which is not
/// available inside hub method invocations. The hub already holds the
/// principal on <see cref="HubCallerContext.User"/>, so going through an
/// ambient HTTP-context accessor would be both wrong and unnecessary.
/// </para>
/// </summary>
[Authorize]
public class ProjectHub(
    IProjectMemberRepository memberRepo,
    ISessionRepository sessions) : Hub<IProjectHubClient>
{
    public async Task JoinProject(Guid projectId)
    {
        var userId = ResolveUserId(Context.User);
        await EnsureProjectMembershipAsync(userId, projectId);
        await Groups.AddToGroupAsync(Context.ConnectionId, HubGroups.Project(projectId));
    }

    public Task LeaveProject(Guid projectId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, HubGroups.Project(projectId));

    public async Task JoinSession(Guid sessionId)
    {
        // Sessions inherit ACL from their parent project — there is no
        // session-level role table. Resolve project, then check membership.
        var session = await sessions.GetByIdAsync(sessionId)
            ?? throw new HubException("Session not found.");
        var userId = ResolveUserId(Context.User);
        await EnsureProjectMembershipAsync(userId, session.ProjectId);
        await Groups.AddToGroupAsync(Context.ConnectionId, HubGroups.Session(sessionId));
    }

    public Task LeaveSession(Guid sessionId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, HubGroups.Session(sessionId));

    public override async Task OnConnectedAsync()
    {
        // Per-user notification group so notifications reach the user no
        // matter which project they're focused on. Always safe to join:
        // the group key includes the connected user's own id.
        var userId = ResolveUserId(Context.User);
        await Groups.AddToGroupAsync(Context.ConnectionId, HubGroups.User(userId));
        await base.OnConnectedAsync();
    }

    private async Task EnsureProjectMembershipAsync(Guid userId, Guid projectId)
    {
        // Same predicate the HTTP-side IProjectAuthorizationService runs;
        // shared via ProjectMembershipPolicy so the rule has one owner.
        var isPlatformAdmin = Context.User?.IsInRole(Roles.PlatformAdmin) == true;
        var member = isPlatformAdmin
            ? null
            : await memberRepo.GetMembershipAsync(userId, projectId);

        if (!ProjectMembershipPolicy.IsAllowed(isPlatformAdmin, member, ProjectRole.Viewer))
            throw new HubException(
                $"User does not have viewer access to project {projectId}.");
    }

    private static Guid ResolveUserId(ClaimsPrincipal? principal)
        => Guid.TryParse(principal?.FindFirst("oid")?.Value, out var id) && id != Guid.Empty
            ? id
            : throw new HubException("Token has no valid object-identifier claim.");
}
