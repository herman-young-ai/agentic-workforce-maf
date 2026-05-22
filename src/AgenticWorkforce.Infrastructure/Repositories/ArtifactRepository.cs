using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Pagination;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AgenticWorkforce.Infrastructure.Repositories;

internal sealed class ArtifactRepository(AppDbContext db) : IArtifactRepository
{
    public Task<ProjectArtifact?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => db.ProjectArtifacts.FirstOrDefaultAsync(a => a.Id == id, ct);

    public Task<PagedResult<ProjectArtifact>> ListByProjectPagedAsync(
        Guid projectId,
        PagedQuery paging,
        CancellationToken ct = default)
        => db.ProjectArtifacts
            .AsNoTracking()
            .Where(a => a.ProjectId == projectId && a.RetractedAt == null)
            .OrderByDescending(a => a.CreatedAt)
            .ToPagedResultAsync(paging, ct);

    public async Task<ArtifactContent?> GetContentAsync(Guid id, CancellationToken ct = default)
    {
        var artifact = await db.ProjectArtifacts
            .AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => new { a.ContentText, a.StorageUrl, a.ContentFormat })
            .FirstOrDefaultAsync(ct);

        return artifact is null
            ? null
            : new ArtifactContent(artifact.ContentText, artifact.StorageUrl, artifact.ContentFormat.ToString());
    }

    public async Task<bool> RetractAsync(Guid id, string retractedBy, CancellationToken ct = default)
    {
        var artifact = await db.ProjectArtifacts.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (artifact is null) return false;
        if (artifact.RetractedAt is not null) return true;

        artifact.RetractedAt = DateTime.UtcNow;
        artifact.RetractedBy = retractedBy;
        await db.SaveChangesAsync(ct);
        return true;
    }
}
