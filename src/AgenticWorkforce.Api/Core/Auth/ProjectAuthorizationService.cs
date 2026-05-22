using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;

namespace AgenticWorkforce.Api.Core.Auth;

public interface IProjectAuthorizationService
{
    Task<bool> HasRoleAsync(Guid userId, Guid projectId, ProjectRole minimumRole, CancellationToken ct = default);
    Task EnsureRoleAsync(Guid userId, Guid projectId, ProjectRole minimumRole, CancellationToken ct = default);
}

internal sealed class ProjectAuthorizationService(
    IProjectMemberRepository memberRepo,
    ICurrentUserAccessor currentUserAccessor) : IProjectAuthorizationService
{
    public async Task<bool> HasRoleAsync(
        Guid userId,
        Guid projectId,
        ProjectRole minimumRole,
        CancellationToken ct = default)
    {
        if (currentUserAccessor.User.IsInRole(Roles.PlatformAdmin))
            return true;

        var member = await memberRepo.GetMembershipAsync(userId, projectId, ct);

        // Direct enum comparison works because ProjectRole values are ordered
        // by seniority (Viewer=10, Operator=20, Reviewer=30, Owner=40). Phase 3.5
        // renumbered the enum and deleted the previous RoleRank switch.
        return member is not null && member.Role >= minimumRole;
    }

    public async Task EnsureRoleAsync(
        Guid userId,
        Guid projectId,
        ProjectRole minimumRole,
        CancellationToken ct = default)
    {
        if (!await HasRoleAsync(userId, projectId, minimumRole, ct))
            throw new ForbiddenException($"User does not have {minimumRole} access to project {projectId}.");
    }
}
