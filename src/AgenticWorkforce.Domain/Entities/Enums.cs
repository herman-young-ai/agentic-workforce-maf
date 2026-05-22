namespace AgenticWorkforce.Domain.Enums;

public enum ProjectStatus { Active, Paused, Completed, Archived }
public enum ProjectTier { User, Platform }

// Integer values are ordered by seniority so `member.Role >= ProjectRole.Reviewer`
// compares correctly without an external rank table. Stored in PostgreSQL as a
// native enum (string), so renumbering has no DB impact.
public enum ProjectRole
{
    Viewer   = 10,
    Operator = 20,
    Reviewer = 30,
    Owner    = 40
}

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
public enum HumanDecisionType { Approved, Rejected, Escalated, Overridden }

public enum EventSeverity { Debug, Info, Warning, Error }
public enum AgentVisibility { Public, Private, Internal }

// Promotion approval state for ProjectLearning. PlatformPromoted bool replaced —
// PromotionStatus == Approved is the single source of truth for a learning's
// platform-level adoption.
public enum PromotionStatus { None, PendingApproval, Approved, Rejected }
