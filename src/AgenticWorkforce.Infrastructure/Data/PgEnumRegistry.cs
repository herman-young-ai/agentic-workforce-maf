using AgenticWorkforce.Domain.Enums;

namespace AgenticWorkforce.Infrastructure.Data;

/// <summary>
/// Single source of truth mapping each CLR enum to its native PostgreSQL
/// enum-type name. Consumed by:
/// <list type="bullet">
///   <item><see cref="DataSourceFactory"/> — registers each enum on the
///     NpgsqlDataSourceBuilder so the Npgsql connector knows the wire format.</item>
///   <item><see cref="InfrastructureServiceExtensions"/> — registers each enum
///     on the NpgsqlDbContextOptionsBuilder so EF Core's type-mapping source
///     resolves CLR enum properties to the native enum type at parameter time
///     (without this EF would fall back to integer parameters).</item>
///   <item><see cref="AppDbContext"/> — declares each enum via
///     <c>HasPostgresEnum&lt;T&gt;()</c> so migrations emit the
///     <c>CREATE TYPE … AS ENUM (…)</c> DDL and pins every CLR enum property to
///     the matching column type.</item>
/// </list>
/// Keep alphabetically grouped by domain area for grep-ability.
/// </summary>
public static class PgEnumRegistry
{
    public static readonly IReadOnlyList<(Type ClrType, string PgEnumName)> All =
    [
        (typeof(ProjectStatus),            "project_status"),
        (typeof(ProjectTier),              "project_tier"),
        (typeof(ProjectRole),              "project_role"),
        (typeof(SystemRole),               "system_role"),
        (typeof(ChangeType),               "change_type"),
        (typeof(IntentSource),             "intent_source"),
        (typeof(AgentRole),                "agent_role"),
        (typeof(TaskType),                 "task_type"),
        (typeof(TaskStatus),               "task_status"),
        (typeof(TaskSource),               "task_source"),
        (typeof(AttemptStatus),            "attempt_status"),
        (typeof(FailureTier),              "failure_tier"),
        (typeof(LearningKind),             "learning_kind"),
        (typeof(LearningStatus),           "learning_status"),
        (typeof(DecisionStatus),           "decision_status"),
        (typeof(ContentFormat),            "content_format"),
        (typeof(ArtifactType),             "artifact_type"),
        (typeof(DocumentType),             "document_type"),
        (typeof(ExtractionStatus),         "extraction_status"),
        (typeof(SessionStatus),            "session_status"),
        (typeof(MessageRole),              "message_role"),
        (typeof(WorkflowRunStatus),        "workflow_run_status"),
        (typeof(HumanInputRequestStatus),  "human_input_request_status"),
        (typeof(HumanDecisionType),        "human_decision_type"),
        (typeof(EventSeverity),            "event_severity"),
        (typeof(AgentVisibility),          "agent_visibility"),
    ];
}
