using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AgenticWorkforce.Api.Core.Auth;

public interface IProjectAuthorizationService
{
    Task<bool> HasRoleAsync(Guid userId, Guid projectId, ProjectRole minimumRole, CancellationToken ct = default);
    Task EnsureRoleAsync(Guid userId, Guid projectId, ProjectRole minimumRole, CancellationToken ct = default);
}

internal sealed class ProjectAuthorizationService(
    AppDbContext db,
    ICurrentUserAccessor currentUserAccessor) : IProjectAuthorizationService
{
    public async Task<bool> HasRoleAsync(Guid userId, Guid projectId, ProjectRole minimumRole, CancellationToken ct = default)
    {
        if (currentUserAccessor.User.IsInRole(Roles.PlatformAdmin))
            return true;

        var member = await db.ProjectMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == userId, ct);

        return member is not null && RoleRank(member.Role) >= RoleRank(minimumRole);
    }

    public async Task EnsureRoleAsync(Guid userId, Guid projectId, ProjectRole minimumRole, CancellationToken ct = default)
    {
        if (!await HasRoleAsync(userId, projectId, minimumRole, ct))
            throw new ForbiddenException($"User does not have {minimumRole} access to project {projectId}.");
    }

    // Enum declaration order: Owner=0, Operator=1, Reviewer=2, Viewer=3
    // Plan hierarchy (most → least): Owner > Reviewer > Operator > Viewer
    private static int RoleRank(ProjectRole role) => role switch
    {
        ProjectRole.Owner    => 4,
        ProjectRole.Reviewer => 3,
        ProjectRole.Operator => 2,
        ProjectRole.Viewer   => 1,
        _                    => 0
    };
}
