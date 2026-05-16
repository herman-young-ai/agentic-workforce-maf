namespace AgenticWorkforce.Domain.Entities;

// -- Project --

public enum ProjectStatus
{
    Draft,
    Active,
    Paused,
    Completed,
    Archived,
    Failed
}

public enum ProjectPriority
{
    Low,
    Medium,
    High,
    Critical
}

public enum SecurityClassification
{
    Internal,
    Confidential,
    Restricted
}

// -- Task --

public enum TaskStatus
{
    Pending,
    Queued,
    Running,
    WaitingApproval,
    Approved,
    Rejected,
    Completed,
    Failed,
    Cancelled,
    Skipped
}

public enum TaskPriority
{
    Low,
    Medium,
    High,
    Critical
}

public enum TaskType
{
    AgentTask,
    HumanTask,
    SystemTask
}

// -- Task Attempt --

public enum AttemptStatus
{
    Running,
    Completed,
    Failed,
    Cancelled
}

// -- Session --

public enum SessionType
{
    Chat,
    Execution,
    Review
}

// -- Session Message --

public enum MessageRole
{
    System,
    User,
    Assistant,
    Tool
}

// -- Agent --

public enum AgentExecutionMode
{
    Sandbox,
    Platform
}

// -- Workflow --

public enum WorkflowNodeType
{
    Start,
    End,
    AgentTask,
    HumanDecision,
    AiDecision,
    Parallel,
    SubWorkflow,
    Action
}

public enum WorkflowExecutionStatus
{
    Pending,
    Running,
    Paused,
    Completed,
    Failed,
    Cancelled
}

public enum WorkflowNodeExecutionStatus
{
    Pending,
    Running,
    WaitingApproval,
    Completed,
    Failed,
    Skipped
}

// -- Learning --

public enum LearningKind
{
    Pcd,
    Finding,
    Preference,
    Procedure,
    Fact
}

public enum LearningStatus
{
    Pending,
    Active,
    Retracted
}

// -- Decision --

public enum DecisionType
{
    Approval,
    Rejection,
    Escalation,
    Override
}

// -- Artifact --

public enum ArtifactType
{
    Report,
    Document,
    Code,
    Data,
    Image,
    Other
}

// -- Project Event --

public enum EventSeverity
{
    Info,
    Warning,
    Error,
    Critical
}

// -- Cost Budget --

public enum BudgetScope
{
    Project,
    Session,
    Agent,
    Execution
}

// -- Platform Role --

public enum PlatformRole
{
    Viewer,
    Operator,
    Reviewer,
    Owner,
    PlatformAdmin
}

// -- Project Role --

public enum ProjectRole
{
    Viewer,
    Operator,
    Reviewer,
    Owner
}
