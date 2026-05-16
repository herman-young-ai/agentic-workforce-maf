# Database Schema Design — EF Core + PostgreSQL

**Source:** R15-response-database-schema.md
**Last updated:** 2026-05-11

---

## 1. Entity Relationship Diagram

```
PLATFORM (no project_id)
========================
User ──1:N── ApiKey
User ──1:N── ProjectMember
User ──1:N── Session
AgentCatalog ──1:N── ProjectAgent
ModelPricing (standalone)
PromptVersion (standalone)

PROJECT SCOPE
=============
Project ──1:1── ProjectContext ──1:N── ContextChange
Project ──1:N── ContextMilestone
Project ──1:N── ProjectIntent (self-FK: revised_from)
Project ──1:N── ProjectAgent ──N:1── AgentCatalog
Project ──1:N── ProjectMember ──N:1── User
Project ──1:N── Task (self-FK: parent_task_id)
                  Task ──1:N── TaskAttempt
                  Task ──1:N── TaskDependency (junction)
                  Task ──1:N── ProjectArtifact
                  Task ──1:N── ProjectEvent
Project ──1:N── ProjectLearning (self-FK: superseded_by, contradicts_id)
Project ──1:N── ProjectDecision (self-FK: superseded_by)
Project ──1:N── MilestoneSummary
Project ──1:N── ProjectDocument ──1:N── DocumentChunk
Project ──1:N── Session ──1:N── SessionMessage
                  Session ──1:N── SessionChannel
Project ──1:N── ProjectEvent
Project ──1:N── WorkflowDefinition ──1:N── WorkflowSchedule
Project ──1:N── WorkflowRun ──1:N── HumanInputRequest
Project ──1:N── LlmCall
```

---

## 2. Base Classes

```csharp
namespace AgenticWorkforce.Domain.Entities;

using System.ComponentModel.DataAnnotations;

public abstract class EntityBase
{
    public Guid Id { get; set; } // UUIDv7, generated client-side by Npgsql 9.0+
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    [Timestamp]
    public uint Version { get; set; } // xmin optimistic concurrency
}

public abstract class ProjectScopedEntity : EntityBase
{
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;
}

public abstract class TaskScopedEntity : ProjectScopedEntity
{
    public Guid TaskId { get; set; }
    public AgenticTask Task { get; set; } = null!;
}
```

---

## 3. Entity Classes

### Project Scope

```csharp
namespace AgenticWorkforce.Domain.Entities;

using System.ComponentModel.DataAnnotations.Schema;
using AgenticWorkforce.Domain.Enums;
using Pgvector;

// ── Project ──────────────────────────────────────────────────

public class Project : EntityBase
{
    public string Name { get; set; } = null!;           // unique
    public string Objective { get; set; } = null!;
    public string? Description { get; set; }
    public string? Brief { get; set; }
    public ProjectStatus Status { get; set; }
    [Column(TypeName = "numeric(12,6)")]
    public decimal? BudgetCeilingUsd { get; set; }
    public string? Jurisdiction { get; set; }
    public string? TemplateName { get; set; }
    public ProjectTier Tier { get; set; }

    // Navigation
    public ProjectContext? Context { get; set; }
    public ICollection<ContextMilestone> Milestones { get; set; } = [];
    public ICollection<ProjectIntent> Intents { get; set; } = [];
    public ICollection<ProjectAgent> Agents { get; set; } = [];
    public ICollection<ProjectMember> Members { get; set; } = [];
    public ICollection<AgenticTask> Tasks { get; set; } = [];
    public ICollection<ProjectLearning> Learnings { get; set; } = [];
    public ICollection<ProjectDecision> Decisions { get; set; } = [];
    public ICollection<MilestoneSummary> MilestoneSummaries { get; set; } = [];
    public ICollection<ProjectArtifact> Artifacts { get; set; } = [];
    public ICollection<ProjectDocument> Documents { get; set; } = [];
    public ICollection<Session> Sessions { get; set; } = [];
    public ICollection<ProjectEvent> Events { get; set; } = [];
    public ICollection<WorkflowDefinition> WorkflowDefinitions { get; set; } = [];
    public ICollection<WorkflowRun> WorkflowRuns { get; set; } = [];
    public ICollection<LlmCall> LlmCalls { get; set; } = [];
}

// ── ProjectContext (PCD) ─────────────────────────────────────

public class ProjectContext : ProjectScopedEntity
{
    [Column(TypeName = "jsonb")]
    public string ContextData { get; set; } = "{}";
    public int ContextVersion { get; set; } = 1;
    public int SizeCharacters { get; set; }
    public int SizeTokens { get; set; }
    public string FormatVersion { get; set; } = "1.0";

    public ICollection<ContextChange> Changes { get; set; } = [];
}

// ── ContextChange ────────────────────────────────────────────

public class ContextChange : ProjectScopedEntity
{
    public Guid ContextId { get; set; }
    public ProjectContext Context { get; set; } = null!;
    public int ContextVersion { get; set; }
    public ChangeType ChangeType { get; set; }
    public string Path { get; set; } = null!;
    [Column(TypeName = "jsonb")]
    public string? OldValue { get; set; }
    [Column(TypeName = "jsonb")]
    public string? NewValue { get; set; }
    public string? AgentName { get; set; }
    public Guid? TaskId { get; set; }
    public string Reason { get; set; } = null!;
}

// ── ContextMilestone ─────────────────────────────────────────

public class ContextMilestone : ProjectScopedEntity
{
    public string Title { get; set; } = null!;
    public string Description { get; set; } = "";
    public int VersionSnapshot { get; set; }
    [Column(TypeName = "jsonb")]
    public string ContextSnapshot { get; set; } = "{}";
    public string CreatedBy { get; set; } = null!;
}

// ── ProjectIntent ────────────────────────────────────────────

public class ProjectIntent : ProjectScopedEntity
{
    public string Intent { get; set; } = null!;
    public string IntentSummary { get; set; } = null!;
    [Column(TypeName = "jsonb")]
    public string Scope { get; set; } = "{}";
    public IntentSource Source { get; set; }
    public Guid? RevisedFromId { get; set; }
    public ProjectIntent? RevisedFrom { get; set; }
    public string Reason { get; set; } = null!;
    public string? AgentName { get; set; }
    public Guid? SessionId { get; set; }
    public Session? Session { get; set; }
}

// ── ProjectAgent (junction) ──────────────────────────────────

public class ProjectAgent : ProjectScopedEntity
{
    public Guid AgentCatalogId { get; set; }
    public AgentCatalog AgentCatalog { get; set; } = null!;
    public AgentRole Role { get; set; }
    public string? UserPrompt { get; set; }
    public bool Enabled { get; set; } = true;
    public int DisplayOrder { get; set; }
    [Column(TypeName = "jsonb")]
    public string? CustomConstraints { get; set; }
}

// ── ProjectMember (junction) ─────────────────────────────────

public class ProjectMember : ProjectScopedEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public ProjectRole Role { get; set; }
}
```

### Task (the primitive)

```csharp
// ── AgenticTask ──────────────────────────────────────────────

public class AgenticTask : ProjectScopedEntity
{
    public TaskType Type { get; set; }
    public TaskStatus Status { get; set; }
    public string Objective { get; set; } = null!;
    public string? AgentName { get; set; }
    public TaskSource Source { get; set; }
    public string? WorkflowNodeId { get; set; }
    public Guid? ParentTaskId { get; set; }
    public AgenticTask? ParentTask { get; set; }
    public Guid? WorkflowRunId { get; set; }
    public WorkflowRun? WorkflowRun { get; set; }
    [Column(TypeName = "jsonb")]
    public string? Inputs { get; set; }
    [Column(TypeName = "jsonb")]
    public string? Outputs { get; set; }
    public string? OutputSummary { get; set; }
    [Column(TypeName = "numeric(12,6)")]
    public decimal CostUsd { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public double? DurationSeconds { get; set; }
    public int RetryCount { get; set; }
    public int MaxRetries { get; set; } = 3;
    public Guid? AssignedToId { get; set; }
    public User? AssignedTo { get; set; }
    public Guid? SessionId { get; set; }
    public Session? Session { get; set; }
    public Guid? CreatedById { get; set; }
    public User? CreatedBy { get; set; }
    public string FormatVersion { get; set; } = "1.0";

    // Navigation
    public ICollection<AgenticTask> ChildTasks { get; set; } = [];
    public ICollection<TaskAttempt> Attempts { get; set; } = [];
    public ICollection<TaskDependency> Dependencies { get; set; } = [];
    public ICollection<TaskDependency> Dependents { get; set; } = [];
    public ICollection<ProjectArtifact> Artifacts { get; set; } = [];
    public ICollection<ProjectEvent> Events { get; set; } = [];
    public ICollection<ProjectLearning> Learnings { get; set; } = [];
    public ICollection<ProjectDecision> Decisions { get; set; } = [];
    public ICollection<HumanInputRequest> HumanInputRequests { get; set; } = [];
}

// ── TaskAttempt ──────────────────────────────────────────────

public class TaskAttempt : TaskScopedEntity
{
    public int AttemptNumber { get; set; }
    public AttemptStatus Status { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public FailureTier? FailureTier { get; set; }
    public string? FailureReason { get; set; }
    public string? FeedbackProvided { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    [Column(TypeName = "numeric(12,6)")]
    public decimal CostUsd { get; set; }
}

// ── TaskDependency (junction) ────────────────────────────────

public class TaskDependency
{
    public Guid TaskId { get; set; }
    public AgenticTask Task { get; set; } = null!;
    public Guid DependsOnTaskId { get; set; }
    public AgenticTask DependsOnTask { get; set; } = null!;
}
```

### Knowledge

```csharp
// ── ProjectLearning ──────────────────────────────────────────

public class ProjectLearning : ProjectScopedEntity
{
    public Guid? TaskId { get; set; }
    public AgenticTask? Task { get; set; }
    public LearningKind Kind { get; set; }
    public string Title { get; set; } = null!;
    public string Body { get; set; } = null!;
    public string? Recommendation { get; set; }
    [Column(TypeName = "numeric(3,2)")]
    public decimal Confidence { get; set; }
    public int OccurrenceCount { get; set; } = 1;
    [Column(TypeName = "jsonb")]
    public string? Evidence { get; set; }
    public string[] AgentNames { get; set; } = [];
    public string[] DomainTags { get; set; } = [];
    public LearningStatus Status { get; set; } = LearningStatus.Active;
    public string? RetractedBy { get; set; }
    public string? RetractedReason { get; set; }
    public Guid? SupersededById { get; set; }
    public ProjectLearning? SupersededBy { get; set; }
    public Guid? ContradictsId { get; set; }
    public ProjectLearning? Contradicts { get; set; }
    public bool PlatformPromoted { get; set; }
    public string? PromotedBy { get; set; }
    public DateTimeOffset? PromotedAt { get; set; }
    public Vector? Embedding { get; set; }
    public string FormatVersion { get; set; } = "1.0";
}

// ── ProjectDecision ──────────────────────────────────────────

public class ProjectDecision : ProjectScopedEntity
{
    public Guid? TaskId { get; set; }
    public AgenticTask? Task { get; set; }
    public string DecisionRef { get; set; } = null!;     // human-readable ID
    public string Domain { get; set; } = null!;
    public string Decision { get; set; } = null!;
    public string Rationale { get; set; } = null!;
    public string MadeBy { get; set; } = null!;
    public Guid? WorkflowRunId { get; set; }
    public WorkflowRun? WorkflowRun { get; set; }
    public DecisionStatus Status { get; set; } = DecisionStatus.Active;
    public Guid? SupersededById { get; set; }
    public ProjectDecision? SupersededBy { get; set; }
}

// ── MilestoneSummary ─────────────────────────────────────────

public class MilestoneSummary : ProjectScopedEntity
{
    public string Title { get; set; } = null!;
    public string Summary { get; set; } = null!;
    [Column(TypeName = "jsonb")]
    public string? WorkflowRunIds { get; set; }
    [Column(TypeName = "jsonb")]
    public string? KeyOutcomes { get; set; }
    public string[] DomainTags { get; set; } = [];
    public DateTimeOffset PeriodStart { get; set; }
    public DateTimeOffset PeriodEnd { get; set; }
}
```

### Artifacts & Documents

```csharp
// ── ProjectArtifact ──────────────────────────────────────────

public class ProjectArtifact : TaskScopedEntity
{
    public string? AgentName { get; set; }
    public ArtifactType ArtifactType { get; set; }
    public string Title { get; set; } = null!;
    public ContentFormat ContentFormat { get; set; }
    public string? ContentText { get; set; }
    public string? StorageUrl { get; set; }
    public long? FileSizeBytes { get; set; }
    public string? ContentHash { get; set; }
    public string? Language { get; set; }
    [Column(TypeName = "jsonb")]
    public string? Metadata { get; set; }
    public string FormatVersion { get; set; } = "1.0";
}

// ── ProjectDocument ──────────────────────────────────────────

public class ProjectDocument : ProjectScopedEntity
{
    public string FileName { get; set; } = null!;
    public string ContentType { get; set; } = null!;     // MIME
    public long FileSizeBytes { get; set; }
    public string StorageUrl { get; set; } = null!;
    public string ContentHash { get; set; } = null!;
    public string? ExtractedText { get; set; }
    public string? ExtractedTextUrl { get; set; }
    public int? PageCount { get; set; }
    public ExtractionStatus ExtractionStatus { get; set; }
    public string? ExtractionError { get; set; }
    public DocumentType DocumentType { get; set; }
    public string? Description { get; set; }
    public string[] Tags { get; set; } = [];
    public bool EmbeddingsGenerated { get; set; }
    public int ChunkCount { get; set; }
    public Guid UploadedById { get; set; }
    public User UploadedBy { get; set; } = null!;

    public ICollection<DocumentChunk> Chunks { get; set; } = [];
}

// ── DocumentChunk ────────────────────────────────────────────

public class DocumentChunk : ProjectScopedEntity
{
    public Guid DocumentId { get; set; }
    public ProjectDocument Document { get; set; } = null!;
    public int ChunkIndex { get; set; }
    public string Content { get; set; } = null!;
    public Vector? Embedding { get; set; }
    public int? PageNumber { get; set; }
    public string? SectionTitle { get; set; }
}
```

### Sessions (transport)

```csharp
// ── Session ──────────────────────────────────────────────────

public class Session : ProjectScopedEntity
{
    public SessionStatus Status { get; set; }
    public Guid? UserId { get; set; }
    public User? User { get; set; }
    public string? AgentName { get; set; }
    public string? Goal { get; set; }
    public string? RollingSummary { get; set; }
    public int? RollingSummaryAnchor { get; set; }
    public int RollingSummaryVersion { get; set; }
    public long TotalInputTokens { get; set; }
    public long TotalOutputTokens { get; set; }
    [Column(TypeName = "numeric(12,6)")]
    public decimal TotalCostUsd { get; set; }
    [Column(TypeName = "numeric(12,6)")]
    public decimal? CostBudgetUsd { get; set; }
    public DateTimeOffset? LastActivityAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }

    public ICollection<SessionMessage> Messages { get; set; } = [];
    public ICollection<SessionChannel> Channels { get; set; } = [];
}

// ── SessionMessage ───────────────────────────────────────────

public class SessionMessage : EntityBase
{
    public Guid SessionId { get; set; }
    public Session Session { get; set; } = null!;
    public MessageRole Role { get; set; }
    public string? Content { get; set; }
    public string? SenderId { get; set; }
    public string? Model { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    [Column(TypeName = "numeric(12,6)")]
    public decimal CostUsd { get; set; }
    public string? Thinking { get; set; }
    public string? ToolName { get; set; }
    public string? ToolCallId { get; set; }
    public string? Status { get; set; }
}

// ── SessionChannel ───────────────────────────────────────────

public class SessionChannel : EntityBase
{
    public Guid SessionId { get; set; }
    public Session Session { get; set; } = null!;
    public string ChannelType { get; set; } = null!;
    public string ChannelId { get; set; } = null!;
    public DateTimeOffset BoundAt { get; set; }
    public bool IsActive { get; set; } = true;
}
```

### Workflows

```csharp
// ── WorkflowDefinition ──────────────────────────────────────

public class WorkflowDefinition : EntityBase
{
    public Guid? ProjectId { get; set; }                 // null = platform template
    public Project? Project { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public int Version { get; set; } = 1;
    public bool Enabled { get; set; } = true;
    [Column(TypeName = "jsonb")]
    public string Nodes { get; set; } = "[]";
    [Column(TypeName = "jsonb")]
    public string Edges { get; set; } = "[]";
    [Column(TypeName = "jsonb")]
    public string? CanvasState { get; set; }
    public string? DesignedBy { get; set; }
    public string? DesignedByAgent { get; set; }
    public DateTimeOffset? LockedAt { get; set; }
    public string FormatVersion { get; set; } = "1.0";

    public ICollection<WorkflowSchedule> Schedules { get; set; } = [];
}

// ── WorkflowRun ──────────────────────────────────────────────

public class WorkflowRun : ProjectScopedEntity
{
    public Guid WorkflowDefinitionId { get; set; }
    public WorkflowDefinition WorkflowDefinition { get; set; } = null!;
    public string WorkflowName { get; set; } = null!;
    public int WorkflowVersion { get; set; }
    public WorkflowRunStatus Status { get; set; }
    public Guid? SessionId { get; set; }
    public Session? Session { get; set; }
    public string? TriggerType { get; set; }
    public string? TriggeredBy { get; set; }
    [Column(TypeName = "jsonb")]
    public string? Context { get; set; }
    [Column(TypeName = "numeric(12,6)")]
    public decimal TotalCostUsd { get; set; }
    [Column(TypeName = "numeric(12,6)")]
    public decimal? BudgetUsd { get; set; }
    [Column(TypeName = "jsonb")]
    public string? ErrorData { get; set; }
    public string? ResultSummary { get; set; }

    public ICollection<AgenticTask> Tasks { get; set; } = [];
    public ICollection<HumanInputRequest> HumanInputRequests { get; set; } = [];
}

// ── WorkflowSchedule ────────────────────────────────────────

public class WorkflowSchedule : ProjectScopedEntity
{
    public Guid WorkflowDefinitionId { get; set; }
    public WorkflowDefinition WorkflowDefinition { get; set; } = null!;
    public string CronExpression { get; set; } = null!;
    public bool Enabled { get; set; } = true;
    public DateTimeOffset? NextRunAt { get; set; }
    public DateTimeOffset? LastRunAt { get; set; }
}

// ── HumanInputRequest ───────────────────────────────────────

public class HumanInputRequest : ProjectScopedEntity
{
    public Guid WorkflowRunId { get; set; }
    public WorkflowRun WorkflowRun { get; set; } = null!;
    public Guid TaskId { get; set; }
    public AgenticTask Task { get; set; } = null!;
    public Guid? SessionId { get; set; }
    public Session? Session { get; set; }
    public string PromptMessage { get; set; } = null!;
    public string? Channel { get; set; }
    [Column(TypeName = "jsonb")]
    public string? Choices { get; set; }
    public HumanInputRequestStatus Status { get; set; }
    public string? Response { get; set; }
    public Guid? ResponderId { get; set; }
    public User? Responder { get; set; }
    public DateTimeOffset? TimeoutAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
}
```

### Events

```csharp
// ── ProjectEvent ─────────────────────────────────────────────

public class ProjectEvent : ProjectScopedEntity
{
    public Guid? TaskId { get; set; }
    public AgenticTask? Task { get; set; }
    public Guid? SessionId { get; set; }
    public Session? Session { get; set; }
    public string EventType { get; set; } = null!;
    public string? Source { get; set; }
    [Column(TypeName = "jsonb")]
    public string? Data { get; set; }
    public EventSeverity Severity { get; set; }
}
```

### Platform Entities

```csharp
// ── User ─────────────────────────────────────────────────────

public class User : EntityBase
{
    public string Email { get; set; } = null!;           // unique
    public string DisplayName { get; set; } = null!;
    public string? HashedPassword { get; set; }
    public SystemRole SystemRole { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsServiceAccount { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }

    public ICollection<ApiKey> ApiKeys { get; set; } = [];
    public ICollection<ProjectMember> Memberships { get; set; } = [];
    public ICollection<Session> Sessions { get; set; } = [];
}

// ── ApiKey ───────────────────────────────────────────────────

public class ApiKey : EntityBase
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string KeyPrefix { get; set; } = null!;
    public string HashedKey { get; set; } = null!;
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    [Column(TypeName = "jsonb")]
    public string? Scopes { get; set; }
}

// ── AgentCatalog ─────────────────────────────────────────────

public class AgentCatalog : EntityBase
{
    public string AgentName { get; set; } = null!;       // unique
    public string? AgentType { get; set; }
    public string? AgentVersion { get; set; }
    public string? Description { get; set; }
    public string? SystemPrompt { get; set; }
    [Column(TypeName = "jsonb")]
    public string? ModelConfig { get; set; }
    [Column(TypeName = "jsonb")]
    public string? Tools { get; set; }
    [Column(TypeName = "jsonb")]
    public string? Scope { get; set; }
    [Column(TypeName = "jsonb")]
    public string? Interface { get; set; }
    [Column(TypeName = "jsonb")]
    public string? Constraints { get; set; }
    public string[] Keywords { get; set; } = [];
    [Column(TypeName = "jsonb")]
    public string? ThinkingBudget { get; set; }
    public bool Enabled { get; set; } = true;
    public bool ChatEnabled { get; set; }
    public AgentVisibility Visibility { get; set; }
    public string? Engine { get; set; }
    public int? MaxInputLength { get; set; }
    [Column(TypeName = "numeric(12,6)")]
    public decimal? MaxBudgetUsd { get; set; }
    public bool ProducesArtifact { get; set; }
    public string? ArtifactType { get; set; }

    public ICollection<ProjectAgent> ProjectAgents { get; set; } = [];
}

// ── PromptVersion ────────────────────────────────────────────

public class PromptVersion : EntityBase
{
    public string EntityType { get; set; } = null!;
    public Guid EntityId { get; set; }
    public string PromptType { get; set; } = null!;
    public string Content { get; set; } = null!;
    public int Version { get; set; }
    public string? ChangedBy { get; set; }
    public string? ChangeReason { get; set; }
}

// ── LlmCall (partitioned) ───────────────────────────────────

public class LlmCall : EntityBase
{
    public Guid? SessionId { get; set; }
    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }
    public Guid? TaskId { get; set; }
    public string? AgentName { get; set; }
    public string? AgentRole { get; set; }
    public string Model { get; set; } = null!;
    public string Provider { get; set; } = null!;
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long CacheReadTokens { get; set; }
    public long CacheCreationTokens { get; set; }
    [Column(TypeName = "numeric(12,6)")]
    public decimal CostUsd { get; set; }
    public int LatencyMs { get; set; }
    public string? RequestId { get; set; }
    public int ToolCount { get; set; }
}

// ── ModelPricing ─────────────────────────────────────────────

public class ModelPricing
{
    public string Model { get; set; } = null!;           // composite PK
    public DateTimeOffset EffectiveFrom { get; set; }    // composite PK
    public DateTimeOffset? EffectiveTo { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    [Column(TypeName = "numeric(12,6)")]
    public decimal PricePerMtokInput { get; set; }
    [Column(TypeName = "numeric(12,6)")]
    public decimal PricePerMtokOutput { get; set; }
    [Column(TypeName = "numeric(12,6)")]
    public decimal PricePerMtokCacheRead { get; set; }
    [Column(TypeName = "numeric(12,6)")]
    public decimal PricePerMtokCacheCreate { get; set; }
}

```

---

## 4. Enumerations

```csharp
namespace AgenticWorkforce.Domain.Enums;

public enum ProjectStatus { Active, Paused, Completed, Archived }
public enum ProjectTier { User, Platform }
public enum ProjectRole { Owner, Operator, Reviewer, Viewer }
public enum SystemRole { PlatformAdmin, Member }
public enum ChangeType { Add, Replace, Remove, Prune, Archive }
public enum IntentSource { UserChat, UserCli, DirectorInferred, System }
public enum AgentRole { Lead, Specialist, Reviewer, Support }

public enum TaskType { AgentTask, HumanDecision, AiDecision, Action, SubWorkflow }
public enum TaskStatus { Proposed, Approved, Queued, Running, Completed, Failed, Skipped, Cancelled }
public enum TaskSource { Workflow, Planner, Manual, AdHoc, Retry, System }

public enum AttemptStatus { Passed, Failed }
public enum FailureTier { Tier1Structural, Tier2Quality, Tier3Integration, AgentError, Timeout }

public enum LearningKind { FailurePattern, SuccessPattern, AntiPattern, RetryStrategy, CapabilityGap, DomainInsight }
public enum LearningStatus { Active, Retracted, Superseded }
public enum DecisionStatus { Active, Superseded, Reversed }

public enum ContentFormat { Markdown, Pptx, Docx, Xlsx, Pdf, Code, Json }
public enum ArtifactType { ResearchReport, VulnerabilityReport, QualityAudit, ArchitectureReview, Report, Code, Data }
public enum DocumentType { Reference, Policy, Data, Report, Code, Other }
public enum ExtractionStatus { Pending, Processing, Completed, Failed }

public enum SessionStatus { Active, Suspended, Completed, Expired, Failed }
public enum MessageRole { User, Assistant, System, ToolCall, ToolResult }

public enum WorkflowRunStatus { Pending, Running, AwaitingInput, Completed, Failed, Cancelled }
public enum HumanInputRequestStatus { Pending, Completed, TimedOut, Cancelled }
public enum EventSeverity { Debug, Info, Warning, Error }
public enum AgentVisibility { Public, Private, Internal }
```

---

## 5. DbContext

```csharp
using Microsoft.EntityFrameworkCore;
using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;
using Pgvector.EntityFrameworkCore;

namespace AgenticWorkforce.Infrastructure.Data;

public class AgenticWorkforceDbContext(DbContextOptions<AgenticWorkforceDbContext> options)
    : DbContext(options)
{
    // Project scope
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectContext> ProjectContexts => Set<ProjectContext>();
    public DbSet<ContextChange> ContextChanges => Set<ContextChange>();
    public DbSet<ContextMilestone> ContextMilestones => Set<ContextMilestone>();
    public DbSet<ProjectIntent> ProjectIntents => Set<ProjectIntent>();
    public DbSet<ProjectAgent> ProjectAgents => Set<ProjectAgent>();
    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();

    // Task
    public DbSet<AgenticTask> Tasks => Set<AgenticTask>();
    public DbSet<TaskAttempt> TaskAttempts => Set<TaskAttempt>();
    public DbSet<TaskDependency> TaskDependencies => Set<TaskDependency>();

    // Knowledge
    public DbSet<ProjectLearning> ProjectLearnings => Set<ProjectLearning>();
    public DbSet<ProjectDecision> ProjectDecisions => Set<ProjectDecision>();
    public DbSet<MilestoneSummary> MilestoneSummaries => Set<MilestoneSummary>();

    // Artifacts & Documents
    public DbSet<ProjectArtifact> ProjectArtifacts => Set<ProjectArtifact>();
    public DbSet<ProjectDocument> ProjectDocuments => Set<ProjectDocument>();
    public DbSet<DocumentChunk> DocumentChunks => Set<DocumentChunk>();

    // Sessions
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<SessionMessage> SessionMessages => Set<SessionMessage>();
    public DbSet<SessionChannel> SessionChannels => Set<SessionChannel>();

    // Workflows
    public DbSet<WorkflowDefinition> WorkflowDefinitions => Set<WorkflowDefinition>();
    public DbSet<WorkflowRun> WorkflowRuns => Set<WorkflowRun>();
    public DbSet<WorkflowSchedule> WorkflowSchedules => Set<WorkflowSchedule>();
    public DbSet<HumanInputRequest> HumanInputRequests => Set<HumanInputRequest>();

    // Events
    public DbSet<ProjectEvent> ProjectEvents => Set<ProjectEvent>();

    // Platform
    public DbSet<User> Users => Set<User>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<AgentCatalog> AgentCatalogs => Set<AgentCatalog>();
    public DbSet<PromptVersion> PromptVersions => Set<PromptVersion>();
    public DbSet<LlmCall> LlmCalls => Set<LlmCall>();
    public DbSet<ModelPricing> ModelPricings => Set<ModelPricing>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ── PostgreSQL enums ─────────────────────────────────
        modelBuilder.HasPostgresEnum<ProjectStatus>();
        modelBuilder.HasPostgresEnum<ProjectTier>();
        modelBuilder.HasPostgresEnum<ProjectRole>();
        modelBuilder.HasPostgresEnum<SystemRole>();
        modelBuilder.HasPostgresEnum<ChangeType>();
        modelBuilder.HasPostgresEnum<IntentSource>();
        modelBuilder.HasPostgresEnum<AgentRole>();
        modelBuilder.HasPostgresEnum<TaskType>();
        modelBuilder.HasPostgresEnum<TaskStatus>();
        modelBuilder.HasPostgresEnum<TaskSource>();
        modelBuilder.HasPostgresEnum<AttemptStatus>();
        modelBuilder.HasPostgresEnum<FailureTier>();
        modelBuilder.HasPostgresEnum<LearningKind>();
        modelBuilder.HasPostgresEnum<LearningStatus>();
        modelBuilder.HasPostgresEnum<DecisionStatus>();
        modelBuilder.HasPostgresEnum<ContentFormat>();
        modelBuilder.HasPostgresEnum<ArtifactType>();
        modelBuilder.HasPostgresEnum<DocumentType>();
        modelBuilder.HasPostgresEnum<ExtractionStatus>();
        modelBuilder.HasPostgresEnum<SessionStatus>();
        modelBuilder.HasPostgresEnum<MessageRole>();
        modelBuilder.HasPostgresEnum<WorkflowRunStatus>();
        modelBuilder.HasPostgresEnum<HumanInputRequestStatus>();
        modelBuilder.HasPostgresEnum<EventSeverity>();
        modelBuilder.HasPostgresEnum<AgentVisibility>();

        // ── pgvector extension ───────────────────────────────
        modelBuilder.HasPostgresExtension("vector");

        // ── Global: xmin concurrency on all EntityBase ───────
        // Exclude append-only partitioned tables — they are never updated,
        // and xmin adds unnecessary overhead on high-volume inserts.
        var appendOnly = new HashSet<Type> { typeof(LlmCall), typeof(ProjectEvent) };
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(EntityBase).IsAssignableFrom(entityType.ClrType)
                && !appendOnly.Contains(entityType.ClrType))
            {
                modelBuilder.Entity(entityType.ClrType)
                    .Property<uint>("Version").IsRowVersion();
            }
        }

        // ── Project ─────────────────────────────────────────
        modelBuilder.Entity<Project>(e =>
        {
            e.ToTable("projects");
            e.HasIndex(p => p.Name).IsUnique();
            e.HasIndex(p => p.Status);
        });

        // ── ProjectContext (1:1 with Project) ────────────────
        modelBuilder.Entity<ProjectContext>(e =>
        {
            e.ToTable("project_contexts");
            e.HasOne(c => c.Project).WithOne(p => p.Context)
                .HasForeignKey<ProjectContext>(c => c.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(c => c.ProjectId).IsUnique();
        });

        // ── ContextChange ────────────────────────────────────
        modelBuilder.Entity<ContextChange>(e =>
        {
            e.ToTable("context_changes");
            e.HasOne(c => c.Project).WithMany()
                .HasForeignKey(c => c.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(c => c.Context).WithMany(pc => pc.Changes)
                .HasForeignKey(c => c.ContextId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(c => c.ContextId);
            e.HasIndex(c => c.ProjectId);
        });

        // ── ContextMilestone ─────────────────────────────────
        modelBuilder.Entity<ContextMilestone>(e =>
        {
            e.ToTable("context_milestones");
            e.HasOne(m => m.Project).WithMany(p => p.Milestones)
                .HasForeignKey(m => m.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(m => m.ProjectId);
        });

        // ── ProjectIntent ────────────────────────────────────
        modelBuilder.Entity<ProjectIntent>(e =>
        {
            e.ToTable("project_intents");
            e.HasOne(i => i.Project).WithMany(p => p.Intents)
                .HasForeignKey(i => i.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(i => i.RevisedFrom).WithMany()
                .HasForeignKey(i => i.RevisedFromId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(i => i.Session).WithMany()
                .HasForeignKey(i => i.SessionId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(i => i.ProjectId);
        });

        // ── ProjectAgent ─────────────────────────────────────
        modelBuilder.Entity<ProjectAgent>(e =>
        {
            e.ToTable("project_agents");
            e.HasOne(a => a.Project).WithMany(p => p.Agents)
                .HasForeignKey(a => a.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(a => a.AgentCatalog).WithMany(c => c.ProjectAgents)
                .HasForeignKey(a => a.AgentCatalogId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(a => new { a.ProjectId, a.AgentCatalogId }).IsUnique();
        });

        // ── ProjectMember ────────────────────────────────────
        modelBuilder.Entity<ProjectMember>(e =>
        {
            e.ToTable("project_members");
            e.HasOne(m => m.Project).WithMany(p => p.Members)
                .HasForeignKey(m => m.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(m => m.User).WithMany(u => u.Memberships)
                .HasForeignKey(m => m.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(m => new { m.ProjectId, m.UserId }).IsUnique();
        });

        // ── AgenticTask ──────────────────────────────────────
        modelBuilder.Entity<AgenticTask>(e =>
        {
            e.ToTable("tasks");
            e.HasOne(t => t.Project).WithMany(p => p.Tasks)
                .HasForeignKey(t => t.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(t => t.ParentTask).WithMany(t => t.ChildTasks)
                .HasForeignKey(t => t.ParentTaskId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(t => t.WorkflowRun).WithMany(r => r.Tasks)
                .HasForeignKey(t => t.WorkflowRunId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(t => t.AssignedTo).WithMany()
                .HasForeignKey(t => t.AssignedToId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(t => t.Session).WithMany()
                .HasForeignKey(t => t.SessionId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(t => t.CreatedBy).WithMany()
                .HasForeignKey(t => t.CreatedById)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(t => new { t.ProjectId, t.Status });
            e.HasIndex(t => new { t.ProjectId, t.CreatedAt });
            e.HasIndex(t => new { t.ProjectId, t.StartedAt });
            e.HasIndex(t => t.WorkflowRunId);
            e.HasIndex(t => t.ParentTaskId);
            e.HasIndex(t => t.AssignedToId);
            e.HasIndex(t => t.SessionId);
        });

        // ── TaskAttempt ──────────────────────────────────────
        modelBuilder.Entity<TaskAttempt>(e =>
        {
            e.ToTable("task_attempts");
            e.HasOne(a => a.Project).WithMany()
                .HasForeignKey(a => a.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(a => a.Task).WithMany(t => t.Attempts)
                .HasForeignKey(a => a.TaskId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(a => new { a.TaskId, a.AttemptNumber }).IsUnique();
            e.HasIndex(a => a.ProjectId);
        });

        // ── TaskDependency ───────────────────────────────────
        modelBuilder.Entity<TaskDependency>(e =>
        {
            e.ToTable("task_dependencies");
            e.HasKey(d => new { d.TaskId, d.DependsOnTaskId });
            e.HasOne(d => d.Task).WithMany(t => t.Dependencies)
                .HasForeignKey(d => d.TaskId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(d => d.DependsOnTask).WithMany(t => t.Dependents)
                .HasForeignKey(d => d.DependsOnTaskId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── ProjectLearning ──────────────────────────────────
        modelBuilder.Entity<ProjectLearning>(e =>
        {
            e.ToTable("project_learnings");
            e.HasOne(l => l.Project).WithMany(p => p.Learnings)
                .HasForeignKey(l => l.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(l => l.Task).WithMany(t => t.Learnings)
                .HasForeignKey(l => l.TaskId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(l => l.SupersededBy).WithMany()
                .HasForeignKey(l => l.SupersededById)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(l => l.Contradicts).WithMany()
                .HasForeignKey(l => l.ContradictsId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(l => new { l.ProjectId, l.Status });
            e.HasIndex(l => l.TaskId);
            e.ToTable(t => t.HasCheckConstraint(
                "ck_project_learnings_confidence", "confidence >= 0 AND confidence <= 1"));
            e.Property(l => l.Embedding).HasColumnType("vector(1536)");
            e.HasIndex(l => l.Embedding)
                .HasMethod("hnsw")
                .HasOperators("vector_cosine_ops");
        });

        // ── ProjectDecision ──────────────────────────────────
        modelBuilder.Entity<ProjectDecision>(e =>
        {
            e.ToTable("project_decisions");
            e.HasOne(d => d.Project).WithMany(p => p.Decisions)
                .HasForeignKey(d => d.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(d => d.Task).WithMany(t => t.Decisions)
                .HasForeignKey(d => d.TaskId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(d => d.WorkflowRun).WithMany()
                .HasForeignKey(d => d.WorkflowRunId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(d => d.SupersededBy).WithMany()
                .HasForeignKey(d => d.SupersededById)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(d => new { d.ProjectId, d.Status });
            e.HasIndex(d => new { d.ProjectId, d.DecisionRef }).IsUnique();
            e.HasIndex(d => d.WorkflowRunId);
        });

        // ── MilestoneSummary ─────────────────────────────────
        modelBuilder.Entity<MilestoneSummary>(e =>
        {
            e.ToTable("milestone_summaries");
            e.HasOne(m => m.Project).WithMany(p => p.MilestoneSummaries)
                .HasForeignKey(m => m.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(m => m.ProjectId);
        });

        // ── ProjectArtifact ──────────────────────────────────
        modelBuilder.Entity<ProjectArtifact>(e =>
        {
            e.ToTable("project_artifacts");
            e.HasOne(a => a.Project).WithMany(p => p.Artifacts)
                .HasForeignKey(a => a.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(a => a.Task).WithMany(t => t.Artifacts)
                .HasForeignKey(a => a.TaskId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(a => a.ProjectId);
            e.HasIndex(a => a.TaskId);
        });

        // ── ProjectDocument ──────────────────────────────────
        modelBuilder.Entity<ProjectDocument>(e =>
        {
            e.ToTable("project_documents");
            e.HasOne(d => d.Project).WithMany(p => p.Documents)
                .HasForeignKey(d => d.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(d => d.UploadedBy).WithMany()
                .HasForeignKey(d => d.UploadedById)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(d => d.ProjectId);
        });

        // ── DocumentChunk ────────────────────────────────────
        modelBuilder.Entity<DocumentChunk>(e =>
        {
            e.ToTable("document_chunks");
            e.HasOne(c => c.Project).WithMany()
                .HasForeignKey(c => c.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(c => c.Document).WithMany(d => d.Chunks)
                .HasForeignKey(c => c.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(c => c.DocumentId);
            e.HasIndex(c => c.ProjectId);
            e.Property(c => c.Embedding).HasColumnType("vector(1536)");
            e.HasIndex(c => c.Embedding)
                .HasMethod("hnsw")
                .HasOperators("vector_cosine_ops");
        });

        // ── Session ──────────────────────────────────────────
        modelBuilder.Entity<Session>(e =>
        {
            e.ToTable("sessions");
            e.HasOne(s => s.Project).WithMany(p => p.Sessions)
                .HasForeignKey(s => s.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(s => s.User).WithMany(u => u.Sessions)
                .HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(s => new { s.ProjectId, s.Status });
            e.HasIndex(s => s.UserId);
        });

        // ── SessionMessage ───────────────────────────────────
        modelBuilder.Entity<SessionMessage>(e =>
        {
            e.ToTable("session_messages");
            e.HasOne(m => m.Session).WithMany(s => s.Messages)
                .HasForeignKey(m => m.SessionId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(m => m.SessionId);
            e.HasIndex(m => new { m.SessionId, m.CreatedAt });
            e.HasIndex(m => m.CreatedAt).HasMethod("brin");
        });

        // ── SessionChannel ───────────────────────────────────
        modelBuilder.Entity<SessionChannel>(e =>
        {
            e.ToTable("session_channels");
            e.HasOne(c => c.Session).WithMany(s => s.Channels)
                .HasForeignKey(c => c.SessionId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(c => c.SessionId);
            e.HasIndex(c => new { c.ChannelType, c.ChannelId });
        });

        // ── WorkflowDefinition ───────────────────────────────
        modelBuilder.Entity<WorkflowDefinition>(e =>
        {
            e.ToTable("workflow_definitions");
            e.HasOne(w => w.Project).WithMany(p => p.WorkflowDefinitions)
                .HasForeignKey(w => w.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(w => w.ProjectId);
            e.HasIndex(w => new { w.ProjectId, w.Name, w.Version }).IsUnique();
            e.HasIndex(w => new { w.Name, w.Version })
                .IsUnique()
                .HasFilter("project_id IS NULL");
        });

        // ── WorkflowRun ──────────────────────────────────────
        modelBuilder.Entity<WorkflowRun>(e =>
        {
            e.ToTable("workflow_runs");
            e.HasOne(r => r.Project).WithMany(p => p.WorkflowRuns)
                .HasForeignKey(r => r.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(r => r.WorkflowDefinition).WithMany()
                .HasForeignKey(r => r.WorkflowDefinitionId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(r => r.Session).WithMany()
                .HasForeignKey(r => r.SessionId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(r => new { r.ProjectId, r.Status });
            e.HasIndex(r => r.WorkflowDefinitionId);
        });

        // ── WorkflowSchedule ─────────────────────────────────
        modelBuilder.Entity<WorkflowSchedule>(e =>
        {
            e.ToTable("workflow_schedules");
            e.HasOne(s => s.Project).WithMany()
                .HasForeignKey(s => s.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(s => s.WorkflowDefinition).WithMany(w => w.Schedules)
                .HasForeignKey(s => s.WorkflowDefinitionId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(s => s.ProjectId);
            e.HasIndex(s => s.WorkflowDefinitionId);
            e.HasIndex(s => new { s.Enabled, s.NextRunAt });
        });

        // ── HumanInputRequest ────────────────────────────────
        modelBuilder.Entity<HumanInputRequest>(e =>
        {
            e.ToTable("human_input_requests");
            e.HasOne(h => h.Project).WithMany()
                .HasForeignKey(h => h.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(h => h.WorkflowRun).WithMany(r => r.HumanInputRequests)
                .HasForeignKey(h => h.WorkflowRunId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(h => h.Task).WithMany(t => t.HumanInputRequests)
                .HasForeignKey(h => h.TaskId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(h => h.Session).WithMany()
                .HasForeignKey(h => h.SessionId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(h => h.Responder).WithMany()
                .HasForeignKey(h => h.ResponderId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(h => new { h.ProjectId, h.Status });
            e.HasIndex(h => h.WorkflowRunId);
            e.HasIndex(h => h.TaskId);
        });

        // ── ProjectEvent (partitioned — DDL via raw SQL migration)
        modelBuilder.Entity<ProjectEvent>(e =>
        {
            e.ToTable("project_events", t => t.ExcludeFromMigrations());
            e.HasKey(ev => new { ev.Id, ev.CreatedAt });
            e.HasOne(ev => ev.Project).WithMany(p => p.Events)
                .HasForeignKey(ev => ev.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(ev => ev.Task).WithMany(t => t.Events)
                .HasForeignKey(ev => ev.TaskId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(ev => ev.Session).WithMany()
                .HasForeignKey(ev => ev.SessionId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ── User ─────────────────────────────────────────────
        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasIndex(u => u.Email).IsUnique();
        });

        // ── ApiKey ───────────────────────────────────────────
        modelBuilder.Entity<ApiKey>(e =>
        {
            e.ToTable("api_keys");
            e.HasOne(k => k.User).WithMany(u => u.ApiKeys)
                .HasForeignKey(k => k.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(k => new { k.UserId, k.Name }).IsUnique();
            e.HasIndex(k => k.KeyPrefix);
        });

        // ── AgentCatalog ─────────────────────────────────────
        modelBuilder.Entity<AgentCatalog>(e =>
        {
            e.ToTable("agent_catalogs");
            e.HasIndex(a => a.AgentName).IsUnique();
        });

        // ── PromptVersion ────────────────────────────────────
        modelBuilder.Entity<PromptVersion>(e =>
        {
            e.ToTable("prompt_versions");
            e.HasIndex(p => new { p.EntityType, p.EntityId, p.PromptType, p.Version }).IsUnique();
        });

        // ── LlmCall (partitioned — DDL via raw SQL migration) ─
        modelBuilder.Entity<LlmCall>(e =>
        {
            e.ToTable("llm_calls", t => t.ExcludeFromMigrations());
            e.HasKey(l => new { l.Id, l.CreatedAt });
            e.HasOne(l => l.Project).WithMany(p => p.LlmCalls)
                .HasForeignKey(l => l.ProjectId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ── ModelPricing ─────────────────────────────────────
        modelBuilder.Entity<ModelPricing>(e =>
        {
            e.ToTable("model_pricing");
            e.HasKey(p => new { p.Model, p.EffectiveFrom });
        });

    }
}
```

**Registration in `Program.cs`:**

```csharp
var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
dataSourceBuilder.UseVector();
dataSourceBuilder.MapEnum<ProjectStatus>();
dataSourceBuilder.MapEnum<ProjectTier>();
dataSourceBuilder.MapEnum<ProjectRole>();
dataSourceBuilder.MapEnum<SystemRole>();
dataSourceBuilder.MapEnum<ChangeType>();
dataSourceBuilder.MapEnum<IntentSource>();
dataSourceBuilder.MapEnum<AgentRole>();
dataSourceBuilder.MapEnum<TaskType>();
dataSourceBuilder.MapEnum<TaskStatus>();
dataSourceBuilder.MapEnum<TaskSource>();
dataSourceBuilder.MapEnum<AttemptStatus>();
dataSourceBuilder.MapEnum<FailureTier>();
dataSourceBuilder.MapEnum<LearningKind>();
dataSourceBuilder.MapEnum<LearningStatus>();
dataSourceBuilder.MapEnum<DecisionStatus>();
dataSourceBuilder.MapEnum<ContentFormat>();
dataSourceBuilder.MapEnum<ArtifactType>();
dataSourceBuilder.MapEnum<DocumentType>();
dataSourceBuilder.MapEnum<ExtractionStatus>();
dataSourceBuilder.MapEnum<SessionStatus>();
dataSourceBuilder.MapEnum<MessageRole>();
dataSourceBuilder.MapEnum<WorkflowRunStatus>();
dataSourceBuilder.MapEnum<HumanInputRequestStatus>();
dataSourceBuilder.MapEnum<EventSeverity>();
dataSourceBuilder.MapEnum<AgentVisibility>();
var dataSource = dataSourceBuilder.Build();

builder.Services.AddDbContext<AgenticWorkforceDbContext>(options =>
    options.UseNpgsql(dataSource));
```

---

## 6. Indexes Summary

| Table | Columns | Type | Unique | Purpose |
|-------|---------|------|--------|---------|
| projects | name | btree | Y | Lookup by name |
| projects | status | btree | N | Filter active projects |
| project_contexts | project_id | btree | Y | 1:1 FK |
| context_changes | context_id | btree | N | FK lookup |
| context_changes | project_id | btree | N | FK lookup |
| context_milestones | project_id | btree | N | FK lookup |
| project_intents | project_id | btree | N | FK lookup |
| project_agents | (project_id, agent_catalog_id) | btree | Y | Prevent duplicate assignment |
| project_members | (project_id, user_id) | btree | Y | Prevent duplicate membership |
| tasks | (project_id, status) | btree | N | Kanban board queries |
| tasks | (project_id, created_at) | btree | N | Timeline queries |
| tasks | (project_id, started_at) | btree | N | Gantt / running-task queries |
| tasks | workflow_run_id | btree | N | FK lookup |
| tasks | parent_task_id | btree | N | FK / tree traversal |
| tasks | assigned_to_id | btree | N | FK lookup |
| tasks | session_id | btree | N | FK lookup |
| task_attempts | (task_id, attempt_number) | btree | Y | Unique attempt per task |
| task_attempts | project_id | btree | N | FK lookup |
| task_dependencies | (task_id, depends_on_task_id) | btree | Y | Composite PK |
| project_learnings | (project_id, status) | btree | N | Active learnings query |
| project_learnings | task_id | btree | N | FK lookup |
| project_learnings | embedding | hnsw | N | Vector similarity search |
| project_decisions | (project_id, status) | btree | N | Active decisions query |
| project_decisions | (project_id, decision_ref) | btree | Y | Unique decision ref per project |
| project_decisions | workflow_run_id | btree | N | FK lookup |
| milestone_summaries | project_id | btree | N | FK lookup |
| project_artifacts | project_id | btree | N | FK lookup |
| project_artifacts | task_id | btree | N | FK lookup |
| project_documents | project_id | btree | N | FK lookup |
| document_chunks | document_id | btree | N | FK lookup |
| document_chunks | project_id | btree | N | FK lookup |
| document_chunks | embedding | hnsw | N | Vector similarity search |
| sessions | (project_id, status) | btree | N | Active sessions query |
| sessions | user_id | btree | N | FK lookup |
| session_messages | session_id | btree | N | FK lookup |
| session_messages | (session_id, created_at) | btree | N | Chronological message load |
| session_messages | created_at | brin | N | Future partition pruning |
| session_channels | session_id | btree | N | FK lookup |
| session_channels | (channel_type, channel_id) | btree | N | Channel routing lookup |
| workflow_definitions | project_id | btree | N | FK lookup |
| workflow_definitions | (project_id, name, version) | btree | Y | Unique workflow per project |
| workflow_definitions | (name, version) WHERE project_id IS NULL | btree | Y | Unique platform templates |
| workflow_runs | (project_id, status) | btree | N | Active runs query |
| workflow_runs | workflow_definition_id | btree | N | FK lookup |
| workflow_schedules | project_id | btree | N | FK lookup |
| workflow_schedules | workflow_definition_id | btree | N | FK lookup |
| workflow_schedules | (enabled, next_run_at) | btree | N | Scheduler polling |
| human_input_requests | (project_id, status) | btree | N | Pending approvals query |
| human_input_requests | workflow_run_id | btree | N | FK lookup |
| human_input_requests | task_id | btree | N | FK lookup |
| users | email | btree | Y | Login lookup |
| api_keys | (user_id, name) | btree | Y | Unique key name per user |
| api_keys | key_prefix | btree | N | API key validation |
| agent_catalogs | agent_name | btree | Y | Agent lookup |
| prompt_versions | (entity_type, entity_id, prompt_type, version) | btree | Y | Version uniqueness |
| llm_calls | project_id | btree | N | FK lookup |
| llm_calls | session_id | btree | N | FK lookup |
| llm_calls | task_id | btree | N | FK lookup |
| llm_calls | created_at | brin | N | Partition pruning |
| llm_calls | (agent_name, created_at) | btree | N | Agent cost analysis |
| project_events | (project_id, created_at) | btree | N | Console timeline |
| project_events | task_id | btree | N | FK lookup |
| project_events | event_type | btree | N | Filter by event type |
| project_events | created_at | brin | N | Partition pruning |

---

## 7. Partition Strategy

| Table | Partition Key | Interval | Retention | Notes |
|-------|--------------|----------|-----------|-------|
| `llm_calls` | `created_at` | Monthly (RANGE) | 12 months hot, archive to Blob, 7-year WORM | BRIN index on `created_at`; `pg_partman` auto-creates partitions; `DROP PARTITION` for instant cleanup |
| `project_events` | `created_at` | Monthly (RANGE) | 12 months hot, archive to Blob | Same strategy as `llm_calls` |

**Raw SQL in migration for partitioned tables** (EF Core cannot express `PARTITION BY`):

```sql
-- Run via migrationBuilder.Sql() in initial migration

-- ── llm_calls (partitioned) ────────────────────────────────
CREATE TABLE llm_calls (
    id UUID NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    session_id UUID,
    project_id UUID,
    task_id UUID,
    agent_name TEXT,
    agent_role TEXT,
    model TEXT NOT NULL,
    provider TEXT NOT NULL,
    input_tokens BIGINT NOT NULL DEFAULT 0,
    output_tokens BIGINT NOT NULL DEFAULT 0,
    cache_read_tokens BIGINT NOT NULL DEFAULT 0,
    cache_creation_tokens BIGINT NOT NULL DEFAULT 0,
    cost_usd NUMERIC(12,6) NOT NULL DEFAULT 0,
    latency_ms INTEGER NOT NULL DEFAULT 0,
    request_id TEXT,
    tool_count INTEGER NOT NULL DEFAULT 0,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (id, created_at)
) PARTITION BY RANGE (created_at);

CREATE INDEX ix_llm_calls_created_at ON llm_calls USING BRIN (created_at);
CREATE INDEX ix_llm_calls_project_id ON llm_calls (project_id);
CREATE INDEX ix_llm_calls_session_id ON llm_calls (session_id);
CREATE INDEX ix_llm_calls_task_id ON llm_calls (task_id);
CREATE INDEX ix_llm_calls_agent_name_created_at ON llm_calls (agent_name, created_at);

SELECT partman.create_parent(
    'public.llm_calls', 'created_at', 'native', 'monthly',
    p_premake := 3, p_start_partition := '2026-01-01'
);

-- ── project_events (partitioned) ───────────────────────────
CREATE TABLE project_events (
    id UUID NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    project_id UUID NOT NULL,
    task_id UUID,
    session_id UUID,
    event_type TEXT NOT NULL,
    source TEXT,
    data JSONB,
    severity event_severity NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (id, created_at)
) PARTITION BY RANGE (created_at);

CREATE INDEX ix_project_events_created_at ON project_events USING BRIN (created_at);
CREATE INDEX ix_project_events_project_created ON project_events (project_id, created_at);
CREATE INDEX ix_project_events_task_id ON project_events (task_id);
CREATE INDEX ix_project_events_event_type ON project_events (event_type);

SELECT partman.create_parent(
    'public.project_events', 'created_at', 'native', 'monthly',
    p_premake := 3, p_start_partition := '2026-01-01'
);
```

EF Core maps to the parent tables transparently. Queries with `WHERE created_at >= ...` benefit from partition pruning automatically.

---

## 8. Migration Strategy

### Initial Migration

```bash
dotnet ef migrations add InitialCreate -p src/AgenticWorkforce.Infrastructure -s src/AgenticWorkforce.Api
dotnet ef database update
```

The migration includes:
1. EF Core auto-generated schema for all non-partitioned tables
2. Raw SQL blocks (`migrationBuilder.Sql()`) for: PostgreSQL enum creation, partitioned table DDL, pg_partman setup, HNSW vector indexes (if the fluent API doesn't emit correct DDL)

### format_version Pattern

Entities with `format_version` (ProjectContext, AgenticTask, ProjectLearning, ProjectArtifact, WorkflowDefinition) use a version string like `"1.0"`. When the JSON schema of their `jsonb` columns changes:

1. Add a new migration that bumps the default `format_version`
2. Old rows keep their original `format_version` — readers branch on version
3. Backfill migration (optional): `UPDATE tasks SET inputs = migrate_v1_to_v2(inputs), format_version = '2.0' WHERE format_version = '1.0'`
4. Never break old readers — only add fields, never remove

### EF Core 10 Migration Path

- `Pgvector.EntityFrameworkCore` 0.3.0 already supports EF Core 9 and 10
- `Npgsql.EntityFrameworkCore.PostgreSQL` 10.x will track EF Core 10 — expect drop-in upgrade
- `ToJson()` complex type mapping may gain first-class collection support in EF Core 10 — watch for migration from `[Column(TypeName = "jsonb")] string` to proper owned types
- PostgreSQL enum mapping API is stable; no breaking changes expected

### Cascade Behaviour Summary

| Parent | Child | On Delete |
|--------|-------|-----------|
| Project | All project-scoped entities | Cascade |
| ProjectContext | ContextChange | Cascade |
| Session | SessionMessage, SessionChannel | Cascade |
| AgenticTask | TaskAttempt, ProjectArtifact, TaskDependency | Cascade |
| AgenticTask | ProjectLearning, ProjectDecision, ProjectEvent | SetNull (task_id) |
| WorkflowDefinition | WorkflowSchedule | Cascade |
| WorkflowRun | HumanInputRequest | Cascade |
| WorkflowDefinition | WorkflowRun | Restrict |
| AgentCatalog | ProjectAgent | Restrict |
| User | ProjectMember | Restrict |
| User | ApiKey | Cascade |
| User | Session | SetNull |
| DocumentChunk | (via Document) | Cascade |

### Task Identity Design Decisions

Lessons from a prior agentic system that used version-prefixed task IDs (`v1-task-001`) and had to migrate to stable UUIDs after encountering severe data integrity problems. These constraints apply to the `AgenticTask` entity and must be preserved.

**1. UUID is the sole task identifier.**

`AgenticTask.Id` (UUIDv7) is the only identity. No secondary human-readable slug, no version prefix, no `task_number` column. Display labels like `task-001` are computed at query time via `ROW_NUMBER()` over `created_at` — never stored. This prevents coupling task identity to plan versions or display ordering.

**2. Display ordering is by `created_at`, not a sort column.**

The planner creates tasks in dependency order, so `created_at` provides natural display ordering. No `sort_order` or `display_order` column exists on `AgenticTask`. If custom ordering is needed in future, it belongs on a view or query, not the entity.

**3. Refinement updates in place — never recreate.**

When a planner refines a failed task, it must `UPDATE` the existing `AgenticTask` row (reset status, update objective/instructions) rather than delete and recreate with a new UUID. This preserves:
- **Execution history:** `TaskAttempt` rows remain linked to the same task UUID across refinements
- **Approvals:** A previously-approved task retains its approval audit trail
- **Artifacts and learnings:** `ProjectArtifact`, `ProjectLearning`, `ProjectDecision` FKs remain valid
- **Dependencies:** `TaskDependency` references from downstream tasks don't break

New tasks added during refinement get new UUIDs. Existing tasks that are unchanged are left untouched. Only failed/modified tasks get updated.

**4. Upstream data flow uses structured JSON references in `Inputs`.**

The `Inputs` jsonb column supports structured references to upstream task outputs:

```json
{
  "source_task": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "source_field": "findings"
}
```

This allows agents to wire task outputs into downstream task inputs by UUID reference. The schema for this is governed by `format_version` — readers branch on version to parse the `Inputs` structure.

**5. CLI uses UUID prefix for disambiguation.**

CLI commands reference tasks by the first 8 characters of the UUID (e.g., `maf task approve a1b2c3d4`). The API resolves this prefix to a full UUID. This keeps the interface human-friendly without storing a secondary identifier.

---

## 9. Knowledge Graph (Apache AGE)

The knowledge graph lives in the same PostgreSQL instance but is managed by Apache AGE, not EF Core. It stores **relationships between entities** — dependency chains, compliance mappings, impact analysis paths. See [ADR-015](ADR-015-knowledge-graph.md) for full design rationale.

### Why AGE, Not EF Core

AGE stores graph data in its own `ag_catalog` schema with vertex/edge tables per label. Queries use openCypher (via `cypher()` SQL function), not LINQ. EF Core cannot model or query graph traversals. The graph layer uses raw `NpgsqlCommand` with the `Npgsql.Age` community package, sharing the same `NpgsqlDataSource` and transactions as EF Core when cross-domain consistency is needed.

### Extension Setup

```sql
-- Enable in Azure PG Flexible Server (requires azure.extensions allowlist)
CREATE EXTENSION IF NOT EXISTS age;
LOAD 'age';
SET search_path = ag_catalog, "$user", public;

-- Single graph for the platform
SELECT create_graph('knowledge_graph');
```

### Vertex Labels

```sql
SELECT create_vlabel('knowledge_graph', 'Component');      -- software component in a project
SELECT create_vlabel('knowledge_graph', 'Policy');          -- regulatory policy/standard (OWASP, PCI, etc.)
SELECT create_vlabel('knowledge_graph', 'Control');         -- specific control within a policy
SELECT create_vlabel('knowledge_graph', 'Finding');         -- security/compliance finding
SELECT create_vlabel('knowledge_graph', 'Agent');           -- agent from AgentCatalog
SELECT create_vlabel('knowledge_graph', 'Library');         -- software dependency (NuGet/npm/pip)
SELECT create_vlabel('knowledge_graph', 'CVE');             -- known vulnerability
SELECT create_vlabel('knowledge_graph', 'OWASPCategory');   -- OWASP Top 10 category
SELECT create_vlabel('knowledge_graph', 'Team');            -- team or member assignment
```

### Vertex Properties (Common)

All vertices carry these standard properties in the AGE `agtype` properties column:

| Property | Type | Purpose |
|----------|------|---------|
| `entity_id` | string (UUID) | Deterministic ID linking to relational entity or serving as dedup key |
| `name` | string | Human-readable name |
| `project_id` | string (UUID) | Scoping — null for global entities (Policy, CVE, OWASPCategory) |
| `entity_table` | string | Optional: which relational table this references |
| `status` | string | `active` or `retracted` |
| `discovered_by_task` | string (UUID) | Which task created this node |
| `updated_at` | string (ISO 8601) | Last modification timestamp |

Label-specific properties:

| Label | Additional Properties |
|-------|---------------------|
| `Component` | `type` (api/service/library/database/queue) |
| `Policy` | `framework` (OWASP/PCI/SOX/POPIA/FCA), `version`, `external_ref` |
| `Control` | `policy_id`, `description`, `category` |
| `Finding` | `severity` (critical/high/medium/low/info), `status` (open/remediated/accepted/retracted) |
| `Library` | `version`, `ecosystem` (nuget/npm/pip) |
| `CVE` | `cve_id`, `severity`, `cvss_score`, `published_at` |
| `OWASPCategory` | `code` (A01-A10), `year` |

### Edge Labels

```sql
SELECT create_elabel('knowledge_graph', 'DEPENDS_ON');      -- Component → Component
SELECT create_elabel('knowledge_graph', 'USES_LIBRARY');    -- Component → Library
SELECT create_elabel('knowledge_graph', 'SCANNED_BY');      -- Component → Agent
SELECT create_elabel('knowledge_graph', 'HAS_FINDING');     -- Component → Finding
SELECT create_elabel('knowledge_graph', 'MAPS_TO');         -- Finding → OWASPCategory
SELECT create_elabel('knowledge_graph', 'REQUIRES');        -- Policy → Control
SELECT create_elabel('knowledge_graph', 'EVIDENCED_BY');    -- Control → Finding
SELECT create_elabel('knowledge_graph', 'PRODUCED_BY');     -- Finding → Task (via entity_id)
SELECT create_elabel('knowledge_graph', 'HAS_VULNERABILITY'); -- Library → CVE
SELECT create_elabel('knowledge_graph', 'REMEDIATES');      -- Finding → Finding
SELECT create_elabel('knowledge_graph', 'ASSIGNED_TO');     -- Component → Team
```

### Edge Properties (Common)

| Property | Type | Purpose |
|----------|------|---------|
| `confidence` | decimal | Extraction confidence (0-1) |
| `discovered_by_task` | string (UUID) | Provenance |
| `discovered_at` | string (ISO 8601) | When the relationship was identified |

Label-specific edge properties:

| Edge Label | Additional Properties |
|------------|---------------------|
| `DEPENDS_ON` | `type` (calls/imports/reads/writes) |
| `USES_LIBRARY` | `version_constraint`, `scope` (runtime/dev/test) |
| `SCANNED_BY` | `last_scan_at`, `task_id` |
| `HAS_FINDING` | `task_id`, `discovered_at` |
| `MAPS_TO` | `confidence` |
| `REQUIRES` | `section_ref` |
| `EVIDENCED_BY` | `evidence_type` (positive/negative/gap) |
| `PRODUCED_BY` | `task_id` |
| `HAS_VULNERABILITY` | `affected_versions`, `fixed_in` |
| `REMEDIATES` | `remediation_type` (fix/mitigate/accept) |

### Indexes

```sql
-- GIN on properties for filtered queries
CREATE INDEX idx_component_props ON knowledge_graph."Component" USING GIN (properties);
CREATE INDEX idx_finding_props ON knowledge_graph."Finding" USING GIN (properties);
CREATE INDEX idx_library_props ON knowledge_graph."Library" USING GIN (properties);

-- Expression BTREE on hot property keys for selective queries
CREATE INDEX idx_component_project ON knowledge_graph."Component"
    USING BTREE ((properties->>'project_id'));
CREATE INDEX idx_finding_severity ON knowledge_graph."Finding"
    USING BTREE ((properties->>'severity'));
CREATE INDEX idx_finding_project ON knowledge_graph."Finding"
    USING BTREE ((properties->>'project_id'));
```

AGE auto-creates BTREE indexes on `id`, `start_id`, and `end_id` for every vertex/edge label.

### Relationship to Relational Entities

The graph does **not** duplicate relational data. Nodes carry `entity_id` references back to relational rows:

```
Graph vertex (Component)          Relational table
  entity_id: "a1b2c3d4..."  ───►  project_artifacts.id OR PCD architecture.components
  name: "PaymentsAPI"
  project_id: "..."

Graph vertex (Finding)
  entity_id: "e5f6a7b8..."  ───►  project_learnings.id (kind=domain_insight)

Graph vertex (Agent)
  entity_id: "c9d0e1f2..."  ───►  agent_catalogs.id
```

Deterministic entity IDs are computed as UUIDv5 from `{label}:{scope}:{name}`. Global entities (Policy, CVE, OWASPCategory) use `scope=global`; project-scoped entities use `scope={project_id}`. This ensures idempotent MERGE operations.

### Example Queries

```sql
-- Blast radius: what depends on this component?
SELECT * FROM cypher('knowledge_graph', $$
    MATCH (s:Component {entity_id: 'a1b2c3d4-...'})
          -[*1..3]->(affected)
    RETURN DISTINCT affected
$$) AS (node agtype);

-- Compliance chain: policy → control → finding
SELECT * FROM cypher('knowledge_graph', $$
    MATCH p = (pol:Policy {name: 'OWASP Top 10 2025'})
              -[:REQUIRES]->(ctrl:Control)
              -[:EVIDENCED_BY]->(f:Finding)
    RETURN p
$$) AS (path agtype);

-- Library impact: what projects use a vulnerable library?
SELECT * FROM cypher('knowledge_graph', $$
    MATCH (lib:Library {name: 'Newtonsoft.Json'})
          <-[:USES_LIBRARY]-(comp:Component)
    RETURN comp.properties->>'name' AS component,
           comp.properties->>'project_id' AS project_id
$$) AS (component agtype, project_id agtype);
```

### C# Access Pattern

```csharp
// Graph queries use raw NpgsqlCommand, NOT EF Core
// Share the same NpgsqlDataSource for connection pooling and transactions

public class AgeKnowledgeGraphRepository : IKnowledgeGraphRepository
{
    private readonly NpgsqlDataSource _dataSource; // same instance as EF Core

    public async Task<IReadOnlyList<GraphPath>> TraverseAsync(
        string startLabel, string startEntityId,
        string? edgeFilter, int maxDepth, CancellationToken ct)
    {
        var edgePattern = edgeFilter is not null ? $":{edgeFilter}" : "";
        var cypher = $"""
            SELECT * FROM cypher('knowledge_graph', $$
                MATCH p = (s:{startLabel} {{entity_id: '{startEntityId}'}})
                          -[{edgePattern}*1..{maxDepth}]->(t)
                RETURN p
            $$) AS (path agtype);
            """;

        await using var cmd = _dataSource.CreateCommand(cypher);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        // ... parse agtype results
    }
}
```

### Backup & DR

No additional infrastructure. AGE data lives in PostgreSQL tables under `ag_catalog`. Existing Flex Server backup, PITR, cross-region read replica, and `pgAudit` logging cover the graph automatically.

### Operational Caveats

| Concern | Impact | Mitigation |
|---------|--------|-----------|
| AGE blocks in-place PG major-version upgrades | Must use dump/restore or logical replication | Same constraint as TimescaleDB; plan upgrades accordingly |
| `Npgsql.Age` is community-maintained | Supply chain risk for regulated workload | Vet and vendor the source; Apache-2.0 licensed |
| No published benchmarks at our exact scale | Performance at 10k-100k nodes / depth 4-5 unvalidated | **PoC mandatory** before production commitment (ADR-015 Phase 1) |

---

### Schema Design Notes

**ContextChange denormalization (intentional).**
`ContextChange` extends `ProjectScopedEntity` (inherits `ProjectId`) and also has a `ContextId` FK. The `ProjectId` is redundant since `ContextId → ProjectContext → ProjectId`, but it is intentionally denormalized for query efficiency — listing all context changes for a project doesn't require a join through `ProjectContext`.

**Inline vectors only — no polymorphic Embedding table.**
The standalone polymorphic `Embedding` table was removed. Vector similarity search uses inline `Vector?` columns on `ProjectLearning` and `DocumentChunk`, each with its own HNSW index. This avoids: orphaned rows from missing FK enforcement, confusion over which table to query, and unnecessary RAM consumption from redundant HNSW indexes. If a future entity needs vector search, add an inline `Vector?` column — it's a one-line migration.

**SessionFollowup deferred to v2.**
The prototype had a `SessionFollowup` entity for agent-scheduled follow-up actions (trigger on event, fire prompt). This is not included in the v1 schema. If follow-up scheduling is needed, it should be modelled as a `TaskType.Action` with a scheduled trigger time, keeping follow-ups within the task primitive rather than introducing a parallel entity.
