using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Services;
using FluentAssertions;
using Xunit;
using TaskStatus = AgenticWorkforce.Domain.Enums.TaskStatus;

namespace AgenticWorkforce.Api.Tests.Unit.Features.Tasks;

public class ApproveTaskTests
{
    // -------------------------------------------------------------------------
    // TaskStateValidator — transition table
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(TaskStatus.Proposed,  TaskStatus.Approved,   true)]
    [InlineData(TaskStatus.Proposed,  TaskStatus.Cancelled,  true)]
    [InlineData(TaskStatus.Proposed,  TaskStatus.Queued,     false)]
    [InlineData(TaskStatus.Proposed,  TaskStatus.Running,    false)]
    [InlineData(TaskStatus.Approved,  TaskStatus.Queued,     true)]
    [InlineData(TaskStatus.Approved,  TaskStatus.Cancelled,  true)]
    [InlineData(TaskStatus.Approved,  TaskStatus.Proposed,   false)]
    [InlineData(TaskStatus.Approved,  TaskStatus.Running,    false)]
    [InlineData(TaskStatus.Queued,    TaskStatus.Running,    true)]
    [InlineData(TaskStatus.Queued,    TaskStatus.Cancelled,  true)]
    [InlineData(TaskStatus.Queued,    TaskStatus.Completed,  false)]
    [InlineData(TaskStatus.Running,   TaskStatus.Completed,  true)]
    [InlineData(TaskStatus.Running,   TaskStatus.Failed,     true)]
    [InlineData(TaskStatus.Running,   TaskStatus.Cancelled,  true)]
    [InlineData(TaskStatus.Running,   TaskStatus.Queued,     false)]
    [InlineData(TaskStatus.Failed,    TaskStatus.Approved,   true)]
    [InlineData(TaskStatus.Failed,    TaskStatus.Running,    false)]
    [InlineData(TaskStatus.Completed, TaskStatus.Running,    false)]
    [InlineData(TaskStatus.Completed, TaskStatus.Proposed,   false)]
    [InlineData(TaskStatus.Cancelled, TaskStatus.Approved,   false)]
    [InlineData(TaskStatus.Cancelled, TaskStatus.Proposed,   false)]
    public void CanTransition_ReturnsExpectedResult(TaskStatus from, TaskStatus to, bool expected) =>
        TaskStateValidator.CanTransition(from, to).Should().Be(expected);

    [Theory]
    [InlineData(TaskStatus.Completed)]
    [InlineData(TaskStatus.Cancelled)]
    [InlineData(TaskStatus.Skipped)]
    public void GetValidTransitions_TerminalStates_ReturnsEmpty(TaskStatus terminal) =>
        TaskStateValidator.GetValidTransitions(terminal).Should().BeEmpty();

    [Fact]
    public void GetValidTransitions_Proposed_IncludesApprovedAndCancelled()
    {
        var transitions = TaskStateValidator.GetValidTransitions(TaskStatus.Proposed);
        transitions.Should().Contain(TaskStatus.Approved);
        transitions.Should().Contain(TaskStatus.Cancelled);
    }

    // -------------------------------------------------------------------------
    // ProjectRole — seniority hierarchy via direct enum order (Phase 3.5)
    // -------------------------------------------------------------------------
    //
    // Phase 3.5 renumbered ProjectRole so integer order matches seniority and
    // deleted the RoleRank switch. These tests assert that direct >= comparison
    // gives the right answer for every (userRole, minimumRole) pair.

    [Fact]
    public void ProjectRole_OwnerOutranksAll()
    {
        ((int)ProjectRole.Owner).Should().BeGreaterThan((int)ProjectRole.Reviewer);
        ((int)ProjectRole.Owner).Should().BeGreaterThan((int)ProjectRole.Operator);
        ((int)ProjectRole.Owner).Should().BeGreaterThan((int)ProjectRole.Viewer);
    }

    [Fact]
    public void ProjectRole_ViewerIsLowest()
    {
        ((int)ProjectRole.Viewer).Should().BeLessThan((int)ProjectRole.Operator);
        ((int)ProjectRole.Viewer).Should().BeLessThan((int)ProjectRole.Reviewer);
        ((int)ProjectRole.Viewer).Should().BeLessThan((int)ProjectRole.Owner);
    }

    // Each row: (userRole, minimumRole, shouldHaveAccess)
    [Theory]
    [InlineData(ProjectRole.Owner,    ProjectRole.Owner,    true)]
    [InlineData(ProjectRole.Owner,    ProjectRole.Reviewer, true)]
    [InlineData(ProjectRole.Owner,    ProjectRole.Operator, true)]
    [InlineData(ProjectRole.Owner,    ProjectRole.Viewer,   true)]
    [InlineData(ProjectRole.Reviewer, ProjectRole.Reviewer, true)]
    [InlineData(ProjectRole.Reviewer, ProjectRole.Operator, true)]
    [InlineData(ProjectRole.Reviewer, ProjectRole.Viewer,   true)]
    [InlineData(ProjectRole.Reviewer, ProjectRole.Owner,    false)]
    [InlineData(ProjectRole.Operator, ProjectRole.Operator, true)]
    [InlineData(ProjectRole.Operator, ProjectRole.Viewer,   true)]
    [InlineData(ProjectRole.Operator, ProjectRole.Reviewer, false)]
    [InlineData(ProjectRole.Operator, ProjectRole.Owner,    false)]
    [InlineData(ProjectRole.Viewer,   ProjectRole.Viewer,   true)]
    [InlineData(ProjectRole.Viewer,   ProjectRole.Operator, false)]
    [InlineData(ProjectRole.Viewer,   ProjectRole.Reviewer, false)]
    [InlineData(ProjectRole.Viewer,   ProjectRole.Owner,    false)]
    public void ProjectRole_HierarchyIsCorrect(ProjectRole userRole, ProjectRole minimumRole, bool hasAccess) =>
        (userRole >= minimumRole).Should().Be(hasAccess);
}
