using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;

namespace AgenticWorkforce.Domain.Queries;

// -----------------------------------------------------------------------------
// Read-model DTOs consumed by repository interfaces.
//
// These records used to live in their corresponding repository interface
// files; that turned Domain.Interfaces.Repositories into a dumping ground for
// query results, filters, and outcome reports. Carving a Queries namespace
// keeps the repository interfaces focused on aggregates while still letting
// CQRS-style read shapes evolve independently.
//
// Grouped by repository area for grep-ability. Keep records minimal —
// reusable across multiple consumers is fine; adding endpoint-specific
// projection types here is not (those belong next to the slice).
// -----------------------------------------------------------------------------

// -- Tasks --------------------------------------------------------------------

/// <summary>
/// Filter options for task list queries. Each property is independent and
/// combined with AND when set.
/// </summary>
public record TaskListFilter(
    TaskStatus? Status = null,
    TaskType? Type = null,
    TaskSource? Source = null,
    string? AgentName = null,
    Guid? ParentTaskId = null);

/// <summary>
/// Per-task outcome of a bulk approve attempt. <see cref="Reason"/> is populated
/// only when <see cref="Approved"/> is false.
/// </summary>
public record BulkApproveItem(Guid TaskId, bool Approved, string? Reason);

public record BulkApproveResult(IReadOnlyList<BulkApproveItem> Items);

// -- Learnings & Documents (vector search results) ----------------------------

/// <summary>
/// Pair of (learning, similarity) returned by vector search. Score is the
/// cosine similarity in [0, 1] with 1 meaning identical embeddings.
/// </summary>
public record LearningMatch(ProjectLearning Learning, double Score);

/// <summary>
/// Pair of (chunk, similarity) returned by vector search across document
/// chunks. <see cref="Score"/> is cosine similarity in [0, 1].
/// </summary>
public record DocumentChunkMatch(DocumentChunk Chunk, double Score);

// -- Artifacts ----------------------------------------------------------------

/// <summary>
/// Carrier for artifact content. <see cref="InlineText"/> is populated for
/// inline artifacts (markdown reports, code files); <see cref="StorageUrl"/>
/// is populated for blob-stored binaries (PDF, DOCX, XLSX). Exactly one is set.
/// </summary>
public record ArtifactContent(string? InlineText, string? StorageUrl, string ContentFormat);

// -- Events -------------------------------------------------------------------

/// <summary>
/// Filter for project event queries. All fields are optional and combined
/// with AND. Date bounds are required only when querying outside the default
/// 7-day window (the partition pruner needs explicit bounds for wider scans).
/// </summary>
public record EventFilter(
    EventSeverity? MinSeverity = null,
    string? EventType = null,
    Guid? TaskId = null,
    Guid? SessionId = null,
    DateTime? Since = null,
    DateTime? Until = null);

// -- Human Input --------------------------------------------------------------

/// <summary>
/// Outcome of a Respond attempt. <see cref="Forbidden"/> is true when the
/// segregation-of-duties check fires (the responder is the user who triggered
/// the workflow run).
/// </summary>
public record RespondOutcome(bool Resolved, bool Forbidden, string? Reason);

// -- Execution dispatch -------------------------------------------------------

/// <summary>
/// State of a dispatched execution. <see cref="Pending"/> means the message
/// is on the queue but no Worker has consumed it yet; <see cref="Picked"/>
/// means a Worker is processing; the others are terminal.
/// </summary>
public enum ExecutionState { Pending, Picked, Completed, Failed }

public record ExecutionStatus(Guid ExecutionId, Guid ProjectId, ExecutionState State);

// -- Project agents -----------------------------------------------------------

/// <summary>
/// Result of a team-seed operation. Reports the IDs created so the caller can
/// respond with concrete attributions without re-reading.
/// </summary>
public record SeededProjectAgent(Guid ProjectAgentId, Guid AgentCatalogId, string AgentName);

// -- Platform-level dashboards ------------------------------------------------

public record PlatformOverview(
    int TotalProjects,
    int ActiveProjects,
    int TotalUsers,
    int ActiveUsers,
    int AgentCatalogSize);
