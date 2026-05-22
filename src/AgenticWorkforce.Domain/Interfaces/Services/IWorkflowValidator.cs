namespace AgenticWorkforce.Domain.Interfaces.Services;

/// <summary>
/// Categorised validation errors. Each cause has a distinct message so tests
/// can assert which rule fired.
/// </summary>
public enum WorkflowValidationCause
{
    /// <summary>Graph has zero or more than one Start node.</summary>
    StartNodeCount,
    /// <summary>Graph has no End node.</summary>
    NoEndNode,
    /// <summary>An edge references a node id that doesn't exist.</summary>
    DanglingEdge,
    /// <summary>One or more nodes are unreachable from the Start node.</summary>
    OrphanNode,
    /// <summary>A Decision node has an outgoing edge with no condition label.</summary>
    DecisionEdgeMissingLabel,
    /// <summary>The graph contains a cycle.</summary>
    Cycle,
    /// <summary>Nodes or edges JSON could not be parsed.</summary>
    MalformedJson
}

public record WorkflowValidationError(WorkflowValidationCause Cause, string Message);

public record WorkflowValidationResult(bool IsValid, IReadOnlyList<WorkflowValidationError> Errors);

/// <summary>
/// Validates a workflow definition's directed graph. Pure function — no I/O,
/// no DB access. Returns a result with one error per distinct rule violation
/// rather than fail-fast, so authors see all problems in one pass.
/// </summary>
public interface IWorkflowValidator
{
    WorkflowValidationResult Validate(string nodesJson, string edgesJson);
}
