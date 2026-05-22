using AgenticWorkforce.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace AgenticWorkforce.Domain.Tests.Unit.Enums;

/// <summary>
/// State machine semantics for <see cref="PromotionStatus"/>. The actual
/// transitions live in <c>ILearningRepository</c>; these tests assert the
/// valid-transition matrix the endpoint handlers gate on.
/// </summary>
public class PromotionStatusTransitionTests
{
    private static bool CanTransition(PromotionStatus from, PromotionStatus to) => (from, to) switch
    {
        // Operator submits a fresh promotion request
        (PromotionStatus.None,            PromotionStatus.PendingApproval) => true,
        // PlatformAdmin decides
        (PromotionStatus.PendingApproval, PromotionStatus.Approved)        => true,
        (PromotionStatus.PendingApproval, PromotionStatus.Rejected)        => true,
        // Operator can resubmit a previously-rejected promotion
        (PromotionStatus.Rejected,        PromotionStatus.PendingApproval) => true,
        // Admin can retract an approved learning back to none
        (PromotionStatus.Approved,        PromotionStatus.None)            => true,
        _                                                                    => false
    };

    [Theory]
    [InlineData(PromotionStatus.None,            PromotionStatus.PendingApproval, true)]
    [InlineData(PromotionStatus.PendingApproval, PromotionStatus.Approved,        true)]
    [InlineData(PromotionStatus.PendingApproval, PromotionStatus.Rejected,        true)]
    [InlineData(PromotionStatus.Rejected,        PromotionStatus.PendingApproval, true)]
    [InlineData(PromotionStatus.Approved,        PromotionStatus.None,            true)]
    // Invalid: cannot skip the pending state
    [InlineData(PromotionStatus.None,            PromotionStatus.Approved,        false)]
    [InlineData(PromotionStatus.None,            PromotionStatus.Rejected,        false)]
    // Invalid: cannot promote without an admin gate
    [InlineData(PromotionStatus.Rejected,        PromotionStatus.Approved,        false)]
    // Invalid: cannot revert to pending from a terminal admin decision
    [InlineData(PromotionStatus.Approved,        PromotionStatus.PendingApproval, false)]
    [InlineData(PromotionStatus.Approved,        PromotionStatus.Rejected,        false)]
    // Self-loops are not transitions
    [InlineData(PromotionStatus.None,            PromotionStatus.None,            false)]
    [InlineData(PromotionStatus.PendingApproval, PromotionStatus.PendingApproval, false)]
    public void Transitions_FollowStateMachine(PromotionStatus from, PromotionStatus to, bool expected) =>
        CanTransition(from, to).Should().Be(expected);
}
