namespace AgenticWorkforce.Domain.Interfaces.Repositories;

/// <summary>
/// State of a dispatched execution. <see cref="Pending"/> means the message
/// is on the queue but no Worker has consumed it yet; <see cref="Picked"/>
/// means a Worker is processing; the others are terminal.
/// </summary>
public enum ExecutionState { Pending, Picked, Completed, Failed }

public record ExecutionStatus(Guid ExecutionId, Guid ProjectId, ExecutionState State);

/// <summary>
/// Repository that wraps the execution-dispatch queue (Redis Stream in
/// production; a no-op stub in Phase 4 that returns synthetic execution
/// IDs). Api enqueues messages here; Worker consumes them in Phase 8 and
/// creates the corresponding WorkflowRun rows. Api never writes WorkflowRun
/// directly (Principle 16: single source of truth per entity).
/// </summary>
public interface IExecutionRepository
{
    /// <summary>
    /// Enqueues a dispatch message for the supplied approved task IDs.
    /// Returns a synthetic execution ID the client can poll via
    /// <see cref="GetStatusAsync"/>.
    /// </summary>
    Task<Guid> EnqueueDispatchAsync(
        Guid projectId,
        IReadOnlyList<Guid> taskIds,
        Guid requestedById,
        CancellationToken ct = default);

    /// <summary>
    /// Enqueues an ad-hoc run-objective message. Returns a synthetic
    /// execution ID.
    /// </summary>
    Task<Guid> EnqueueAdHocAsync(
        Guid projectId,
        string objective,
        Guid requestedById,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the current execution status, or null if no message with that
    /// ID was ever enqueued.
    /// </summary>
    Task<ExecutionStatus?> GetStatusAsync(Guid executionId, CancellationToken ct = default);
}
