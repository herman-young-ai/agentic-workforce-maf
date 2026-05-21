using AgenticWorkforce.Domain.Enums;

namespace AgenticWorkforce.Domain.Services;

/// <summary>
/// Pure state-machine rules for task lifecycle transitions.
/// No infrastructure dependencies — safe to call from handlers, validators, and unit tests.
/// </summary>
public static class TaskStateValidator
{
    private static readonly IReadOnlyDictionary<TaskStatus, TaskStatus[]> ValidTransitions =
        new Dictionary<TaskStatus, TaskStatus[]>
        {
            [TaskStatus.Proposed]  = [TaskStatus.Approved, TaskStatus.Cancelled],
            [TaskStatus.Approved]  = [TaskStatus.Queued,   TaskStatus.Cancelled],
            [TaskStatus.Queued]    = [TaskStatus.Running,  TaskStatus.Cancelled],
            [TaskStatus.Running]   = [TaskStatus.Completed, TaskStatus.Failed, TaskStatus.Cancelled],
            [TaskStatus.Failed]    = [TaskStatus.Approved],
            [TaskStatus.Completed] = [],
            [TaskStatus.Cancelled] = [],
            [TaskStatus.Skipped]   = []
        };

    public static bool CanTransition(TaskStatus from, TaskStatus to) =>
        ValidTransitions.TryGetValue(from, out var valid) && valid.Contains(to);

    public static IReadOnlyList<TaskStatus> GetValidTransitions(TaskStatus from) =>
        ValidTransitions.TryGetValue(from, out var valid) ? valid : [];
}
