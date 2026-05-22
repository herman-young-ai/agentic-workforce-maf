using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Pagination;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AgenticWorkforce.Infrastructure.Repositories;

internal sealed class ProjectRepository(AppDbContext db) : IProjectRepository
{
    public Task<Project?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => db.Projects
            .Include(p => p.Members)
            .Include(p => p.Agents)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<PagedResult<Project>> ListByMemberPagedAsync(
        Guid userId,
        ProjectStatus? statusFilter,
        PagedQuery paging,
        CancellationToken ct = default)
    {
        var query = db.Projects
            .AsNoTracking()
            .Where(p => p.Members.Any(m => m.UserId == userId));

        if (statusFilter.HasValue)
            query = query.Where(p => p.Status == statusFilter.Value);

        return query
            .OrderByDescending(p => p.CreatedAt)
            .ToPagedResultAsync(paging, ct);
    }

    public Task<bool> ExistsByNameAsync(string name, CancellationToken ct = default)
        => db.Projects.AnyAsync(p => p.Name == name, ct);

    public async Task<Project> CreateWithOwnerAsync(Project project, Guid ownerUserId, CancellationToken ct = default)
    {
        var ownerMember = new ProjectMember
        {
            Project = project,
            UserId  = ownerUserId,
            Role    = ProjectRole.Owner
        };
        db.Projects.Add(project);
        db.ProjectMembers.Add(ownerMember);
        await db.SaveChangesAsync(ct);
        return project;
    }

    public async Task UpdateAsync(Project project, CancellationToken ct = default)
    {
        db.Projects.Update(project);
        await db.SaveChangesAsync(ct);
    }
}
