namespace AgenticWorkforce.Domain.Events;

/// <summary>
/// Canonical event-type strings emitted by IEventPublisher and consumed by
/// SignalR/SSE clients. Pure constants — no behaviour, no dependencies —
/// so the Worker, Api, and any future SDK consumer all reference the same
/// vocabulary.
/// <para>
/// Format is <c>{aggregate}.{verb_past_tense}</c>. The aggregate prefix
/// (project, task, agent, workflow, …) lets clients route by topic without
/// parsing the suffix; the verb tense is past so the type name reads as a
/// fact ("task.approved" — the approval already happened) rather than an
/// intent. Add new types here; do not invent ad-hoc strings at call sites.
/// </para>
/// </summary>
public static class EventTypes
{
    // -- Project lifecycle ---------------------------------------------------
    public const string ProjectCreated  = "project.created";
    public const string ProjectPaused   = "project.paused";
    public const string ProjectResumed  = "project.resumed";
    public const string ProjectArchived = "project.archived";

    // -- Task lifecycle ------------------------------------------------------
    public const string TaskCreated   = "task.created";
    public const string TaskApproved  = "task.approved";
    public const string TaskRejected  = "task.rejected";
    public const string TaskQueued    = "task.queued";
    public const string TaskStarted   = "task.started";
    public const string TaskCompleted = "task.completed";
    public const string TaskFailed    = "task.failed";
    public const string TaskCancelled = "task.cancelled";
    public const string TaskRetried   = "task.retried";

    // -- Agent execution -----------------------------------------------------
    public const string AgentStarted    = "agent.started";
    public const string AgentCompleted  = "agent.completed";
    public const string AgentFailed     = "agent.failed";
    public const string AgentToolCall   = "agent.tool_call";
    public const string AgentTokenChunk = "agent.token_chunk";

    // -- Workflow ------------------------------------------------------------
    public const string WorkflowStarted   = "workflow.started";
    public const string WorkflowCompleted = "workflow.completed";
    public const string WorkflowFailed    = "workflow.failed";
    public const string WorkflowPaused    = "workflow.paused";

    // -- Human input ---------------------------------------------------------
    public const string HumanInputRequired = "human_input.required";
    // Data payload carries the HumanDecisionType (Approved / Rejected /
    // Escalated / Overridden) so the client can render the outcome.
    public const string HumanInputProvided = "human_input.provided";
    public const string HumanInputTimedOut = "human_input.timed_out";

    // -- Budget --------------------------------------------------------------
    public const string BudgetWarning   = "budget.warning";
    public const string BudgetExhausted = "budget.exhausted";

    // -- Knowledge -----------------------------------------------------------
    public const string LearningExtracted = "learning.extracted";
    public const string LearningRetracted = "learning.retracted";
    public const string ContextUpdated    = "context.updated";

    // -- Session -------------------------------------------------------------
    public const string SessionCreated   = "session.created";
    public const string SessionCompleted = "session.completed";
    public const string MessageReceived  = "message.received";
}
