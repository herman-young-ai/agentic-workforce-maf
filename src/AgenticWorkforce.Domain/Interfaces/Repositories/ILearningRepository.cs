using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Pagination;
using Pgvector;

namespace AgenticWorkforce.Domain.Interfaces.Repositories;

/// <summary>
/// Pair of (learning, similarity) returned by vector search. Score is the
/// cosine similarity in [0, 1] with 1 meaning identical embeddings.
/// </summary>
public record LearningMatch(ProjectLearning Learning, double Score);

/// <summary>
/// Repository for accumulated project learnings. Includes the promotion
/// approval state machine (Pending -> Approved | Rejected) added in
/// Phase 3.5.
/// </summary>
public interface ILearningRepository
{
    Task<ProjectLearning?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<PagedResult<ProjectLearning>> ListByProjectPagedAsync(
        Guid projectId,
        PagedQuery paging,
        CancellationToken ct = default);

    Task<ProjectLearning> AddAsync(ProjectLearning learning, CancellationToken ct = default);

    Task UpdateAsync(ProjectLearning learning, CancellationToken ct = default);

    Task RetractAsync(Guid id, string retractedBy, string reason, CancellationToken ct = default);

    /// <summary>
    /// Marks <paramref name="oldId"/> superseded by a new learning (provided)
    /// in a single transaction. The new learning is added and the old one's
    /// SupersededById/Status are updated to point at it.
    /// </summary>
    Task SupersedeAsync(
        Guid oldId,
        ProjectLearning replacement,
        CancellationToken ct = default);

    Task RequestPromotionAsync(Guid id, Guid requestedById, CancellationToken ct = default);

    Task ApprovePromotionAsync(Guid id, Guid approvedById, CancellationToken ct = default);

    Task RejectPromotionAsync(Guid id, string reason, CancellationToken ct = default);

    Task<PagedResult<ProjectLearning>> ListPendingPromotionsPagedAsync(
        PagedQuery paging,
        CancellationToken ct = default);

    /// <summary>
    /// Vector similarity search across the project's learnings. Caller must
    /// have already obtained a query embedding via <c>IEmbeddingService</c>.
    /// </summary>
    Task<IReadOnlyList<LearningMatch>> SearchByEmbeddingAsync(
        Guid projectId,
        Vector queryEmbedding,
        int limit,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the top-K nearest neighbours of <paramref name="learningId"/>
    /// excluding the learning itself.
    /// </summary>
    Task<IReadOnlyList<LearningMatch>> FindSimilarAsync(
        Guid learningId,
        int limit,
        CancellationToken ct = default);
}
