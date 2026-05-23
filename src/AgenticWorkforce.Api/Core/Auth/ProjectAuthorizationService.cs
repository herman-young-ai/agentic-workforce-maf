using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Services;

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
        // PlatformAdmin lookup is the only HTTP-context-bound input here.
        // The actual rule lives in ProjectMembershipPolicy so the hub
        // (which has no HttpContext) can apply the same predicate.
        var isPlatformAdmin = currentUserAccessor.User.IsInRole(Roles.PlatformAdmin);
        var member = isPlatformAdmin
            ? null  // skip the DB hit — PlatformAdmin bypass wins regardless
            : await memberRepo.GetMembershipAsync(userId, projectId, ct);

        return ProjectMembershipPolicy.IsAllowed(isPlatformAdmin, member, minimumRole);
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
