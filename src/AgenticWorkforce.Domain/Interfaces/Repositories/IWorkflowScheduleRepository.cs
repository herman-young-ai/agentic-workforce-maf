using AgenticWorkforce.Domain.Entities;

namespace AgenticWorkforce.Domain.Interfaces.Repositories;

/// <summary>
/// Repository for cron-driven workflow schedules.
/// </summary>
public interface IWorkflowScheduleRepository
{
    Task<WorkflowSchedule?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<WorkflowSchedule>> ListByProjectAsync(Guid projectId, CancellationToken ct = default);

    /// <summary>
    /// Schedules with <c>Enabled=true</c> and <c>NextRunAt</c> within
    /// <paramref name="horizon"/> of <c>DateTime.UtcNow</c>, ordered by
    /// <c>NextRunAt</c> ascending.
    /// </summary>
    Task<IReadOnlyList<WorkflowSchedule>> ListUpcomingAsync(
        Guid projectId,
        TimeSpan horizon,
        CancellationToken ct = default);

    Task<WorkflowSchedule> AddAsync(WorkflowSchedule schedule, CancellationToken ct = default);

    Task UpdateAsync(WorkflowSchedule schedule, CancellationToken ct = default);

    Task<bool> RemoveAsync(Guid id, CancellationToken ct = default);
}
