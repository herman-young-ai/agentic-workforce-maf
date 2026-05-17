using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AgenticWorkforce.Infrastructure.Repositories;

internal sealed class ProjectRepository(AppDbContext db) : IProjectRepository
{
    public async Task<Project?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await db.Projects
            .Include(p => p.Members)
            .Include(p => p.Agents)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<IReadOnlyList<Project>> ListByMemberAsync(Guid userId, CancellationToken ct = default)
        => await db.Projects
            .Where(p => p.Members.Any(m => m.UserId == userId))
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct);

    public async Task<Project> CreateAsync(Project project, CancellationToken ct = default)
    {
        db.Projects.Add(project);
        await db.SaveChangesAsync(ct);
        return project;
    }

    public async Task<Project> UpdateAsync(Project project, CancellationToken ct = default)
    {
        db.Projects.Update(project);
        await db.SaveChangesAsync(ct);
        return project;
    }

    public Task<bool> ExistsByNameAsync(string name, CancellationToken ct = default)
        => db.Projects.AnyAsync(p => p.Name == name, ct);
}
