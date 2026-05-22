using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AgenticWorkforce.Infrastructure.Repositories;

internal sealed class ProjectMemberRepository(AppDbContext db) : IProjectMemberRepository
{
    public Task<ProjectMember?> GetMembershipAsync(
        Guid userId,
        Guid projectId,
        CancellationToken ct = default)
        => db.ProjectMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == userId, ct);

    public async Task<IReadOnlyList<ProjectMember>> ListByProjectAsync(
        Guid projectId,
        CancellationToken ct = default)
        => await db.ProjectMembers
            .AsNoTracking()
            .Include(m => m.User)
            .Where(m => m.ProjectId == projectId)
            .OrderBy(m => m.Role)
            .ToListAsync(ct);

    public async Task<ProjectMember> AddAsync(ProjectMember member, CancellationToken ct = default)
    {
        db.ProjectMembers.Add(member);
        await db.SaveChangesAsync(ct);
        return member;
    }

    public async Task UpdateAsync(ProjectMember member, CancellationToken ct = default)
    {
        db.ProjectMembers.Update(member);
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> RemoveAsync(Guid projectId, Guid userId, CancellationToken ct = default)
    {
        var member = await db.ProjectMembers
            .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == userId, ct);
        if (member is null)
            return false;

        if (member.Role == ProjectRole.Owner)
        {
            var ownerCount = await db.ProjectMembers
                .CountAsync(m => m.ProjectId == projectId && m.Role == ProjectRole.Owner, ct);
            if (ownerCount <= 1)
                return false;
        }

        db.ProjectMembers.Remove(member);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task TransferOwnershipAsync(
        Guid projectId,
        Guid currentOwnerId,
        Guid newOwnerId,
        CancellationToken ct = default)
    {
        var currentOwner = await db.ProjectMembers
            .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == currentOwnerId, ct)
            ?? throw new NotFoundException("Member", currentOwnerId);

        var newOwnerMember = await db.ProjectMembers
            .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == newOwnerId, ct)
            ?? throw new NotFoundException("Member", newOwnerId);

        currentOwner.Role = ProjectRole.Operator;
        newOwnerMember.Role = ProjectRole.Owner;

        await db.SaveChangesAsync(ct);
    }
}
