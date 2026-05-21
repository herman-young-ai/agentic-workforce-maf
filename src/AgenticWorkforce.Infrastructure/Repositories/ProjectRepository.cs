using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Interfaces.Repositories;
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

    public async Task<IReadOnlyList<Project>> ListByMemberAsync(Guid userId, CancellationToken ct = default)
        => await db.Projects
            .AsNoTracking()
            .Where(p => p.Members.Any(m => m.UserId == userId))
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct);

    public Task<bool> ExistsByNameAsync(string name, CancellationToken ct = default)
        => db.Projects.AnyAsync(p => p.Name == name, ct);
}
