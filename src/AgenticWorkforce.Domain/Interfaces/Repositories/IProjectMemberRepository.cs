using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;

namespace AgenticWorkforce.Domain.Interfaces.Repositories;

/// <summary>
/// Repository for ProjectMember rows — the join between Users and Projects
/// carrying the project role. ProjectAuthorizationService consults this
/// repository for every endpoint authorization check, so the implementation
/// should cache hot reads (5-min TTL per (userId, projectId), invalidated on
/// mutations).
/// </summary>
public interface IProjectMemberRepository
{
    /// <summary>
    /// Returns the membership row or null if the user is not a member of the
    /// project. Caller compares the row's <see cref="ProjectMember.Role"/>
    /// against the minimum required role.
    /// </summary>
    Task<ProjectMember?> GetMembershipAsync(Guid userId, Guid projectId, CancellationToken ct = default);

    Task<IReadOnlyList<ProjectMember>> ListByProjectAsync(Guid projectId, CancellationToken ct = default);

    Task<ProjectMember> AddAsync(ProjectMember member, CancellationToken ct = default);

    Task UpdateAsync(ProjectMember member, CancellationToken ct = default);

    /// <summary>
    /// Removes a member. Returns false if no such membership exists. Returns
    /// false (and does NOT remove) if the only Owner would be removed.
    /// </summary>
    Task<bool> RemoveAsync(Guid projectId, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Demotes the current owner to <see cref="ProjectRole.Operator"/> and
    /// promotes the target user to Owner in a single transaction. Throws
    /// <see cref="Exceptions.NotFoundException"/> if either membership is
    /// missing.
    /// </summary>
    Task TransferOwnershipAsync(
        Guid projectId,
        Guid currentOwnerId,
        Guid newOwnerId,
        CancellationToken ct = default);
}
