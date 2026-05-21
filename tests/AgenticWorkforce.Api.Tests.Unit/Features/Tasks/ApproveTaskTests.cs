using AgenticWorkforce.Api.Core.Auth;
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
    // ProjectAuthorizationService.RoleRank — permission hierarchy
    // -------------------------------------------------------------------------

    [Fact]
    public void RoleRank_OwnerOutranksAll()
    {
        var ownerRank = ProjectAuthorizationService.RoleRank(ProjectRole.Owner);

        ownerRank.Should().BeGreaterThan(ProjectAuthorizationService.RoleRank(ProjectRole.Reviewer));
        ownerRank.Should().BeGreaterThan(ProjectAuthorizationService.RoleRank(ProjectRole.Operator));
        ownerRank.Should().BeGreaterThan(ProjectAuthorizationService.RoleRank(ProjectRole.Viewer));
    }

    [Fact]
    public void RoleRank_ViewerIsLowest()
    {
        var viewerRank = ProjectAuthorizationService.RoleRank(ProjectRole.Viewer);

        viewerRank.Should().BeLessThan(ProjectAuthorizationService.RoleRank(ProjectRole.Operator));
        viewerRank.Should().BeLessThan(ProjectAuthorizationService.RoleRank(ProjectRole.Reviewer));
        viewerRank.Should().BeLessThan(ProjectAuthorizationService.RoleRank(ProjectRole.Owner));
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
    public void RoleRank_HierarchyIsCorrect(ProjectRole userRole, ProjectRole minimumRole, bool hasAccess)
    {
        var result = ProjectAuthorizationService.RoleRank(userRole)
                  >= ProjectAuthorizationService.RoleRank(minimumRole);

        result.Should().Be(hasAccess);
    }
}
