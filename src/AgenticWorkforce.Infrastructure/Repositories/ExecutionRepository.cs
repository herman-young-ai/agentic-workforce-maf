using System.Collections.Concurrent;
using AgenticWorkforce.Domain.Interfaces.Repositories;

namespace AgenticWorkforce.Infrastructure.Repositories;

/// <summary>
/// Phase 4 in-memory stub. Records enqueued messages so <c>GetStatusAsync</c>
/// can answer for known IDs (always with state <see cref="ExecutionState.Pending"/>
/// because no consumer is wired yet). Phase 5/8 replaces this with a Redis
/// Streams client; the interface is stable across the swap.
/// </summary>
internal sealed class InMemoryExecutionRepository : IExecutionRepository
{
    private readonly ConcurrentDictionary<Guid, ExecutionStatus> _enqueued = new();

    public Task<Guid> EnqueueDispatchAsync(
        Guid projectId,
        IReadOnlyList<Guid> taskIds,
        Guid requestedById,
        CancellationToken ct = default)
    {
        var id = Guid.NewGuid();
        _enqueued[id] = new ExecutionStatus(id, projectId, ExecutionState.Pending);
        return Task.FromResult(id);
    }

    public Task<Guid> EnqueueAdHocAsync(
        Guid projectId,
        string objective,
        Guid requestedById,
        CancellationToken ct = default)
    {
        var id = Guid.NewGuid();
        _enqueued[id] = new ExecutionStatus(id, projectId, ExecutionState.Pending);
        return Task.FromResult(id);
    }

    public Task<ExecutionStatus?> GetStatusAsync(Guid executionId, CancellationToken ct = default)
        => Task.FromResult(_enqueued.TryGetValue(executionId, out var status) ? status : null);
}
