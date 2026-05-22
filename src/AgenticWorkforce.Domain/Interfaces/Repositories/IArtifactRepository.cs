using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Pagination;

namespace AgenticWorkforce.Domain.Interfaces.Repositories;

/// <summary>
/// Carrier for artifact content. <see cref="InlineText"/> is populated for
/// inline artifacts (markdown reports, code files); <see cref="StorageUrl"/>
/// is populated for blob-stored binaries (PDF, DOCX, XLSX). Exactly one is set.
/// </summary>
public record ArtifactContent(string? InlineText, string? StorageUrl, string ContentFormat);

/// <summary>
/// Repository for project artifacts — the polished deliverables that
/// stakeholders care about (reports, generated documents, code).
/// </summary>
public interface IArtifactRepository
{
    Task<ProjectArtifact?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<PagedResult<ProjectArtifact>> ListByProjectPagedAsync(
        Guid projectId,
        PagedQuery paging,
        CancellationToken ct = default);

    Task<ArtifactContent?> GetContentAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Soft-retract an artifact (Principle 13: never truly delete a
    /// deliverable). Returns false if the artifact does not exist.
    /// </summary>
    Task<bool> RetractAsync(Guid id, string retractedBy, CancellationToken ct = default);
}
