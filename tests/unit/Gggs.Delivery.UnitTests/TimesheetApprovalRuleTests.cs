using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Modules.Timesheets.Domain;
using Shouldly;

namespace Gggs.Delivery.UnitTests;

public sealed class TimesheetApprovalRuleTests
{
    private static readonly DateTime Now = new(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc);

    private static TimesheetApproval NewShare() => TimesheetApproval.Open(Timesheet.Open(Guid.NewGuid(), new DateOnly(2026, 11, 2)), Guid.NewGuid());

    private static TimesheetApproval Submitted()
    {
        TimesheetApproval share = NewShare();
        share.Submit(8, 8, Guid.NewGuid(), Now);
        return share;
    }

    [Theory]
    [InlineData(TimesheetStatus.Approved, false, ApprovalStatus.Approved, ApprovalStatus.Approved)]
    [InlineData(TimesheetStatus.Submitted, false, ApprovalStatus.Approved, ApprovalStatus.Submitted)]
    [InlineData(TimesheetStatus.Rejected, false, ApprovalStatus.Rejected, ApprovalStatus.Submitted)]
    [InlineData(TimesheetStatus.Rejected, false, ApprovalStatus.Approved, ApprovalStatus.Rejected)]
    [InlineData(TimesheetStatus.Draft, false, ApprovalStatus.Approved, ApprovalStatus.Returned)]
    [InlineData(TimesheetStatus.Draft, true, ApprovalStatus.Approved, ApprovalStatus.Approved)]
    public void AWeek_TakesTheStatusItsSharesAddUpTo(TimesheetStatus expected, bool hoursInNoShare, ApprovalStatus first, ApprovalStatus second)
        => TimesheetApprovals.WeekStatus([first, second], hoursInNoShare).ShouldBe(expected);

    [Fact]
    public void AWeekWithNoShares_IsADraft()
        => TimesheetApprovals.WeekStatus([], hasHoursInNoShare: false).ShouldBe(TimesheetStatus.Draft);

    [Fact]
    public void SubmittedAndApprovedHours_AreLocked_OthersAreNot()
    {
        TimesheetApproval share = NewShare();
        share.IsLocked.ShouldBeFalse();

        share.Submit(8, 8, null, Now);
        share.IsLocked.ShouldBeTrue();

        share.Approve(Now);
        share.IsLocked.ShouldBeTrue();

        share.Reopen();
        share.IsLocked.ShouldBeFalse();
    }

    [Fact]
    public void LockedHours_CannotBeSubmittedAgain()
        => Should.Throw<DomainRuleException>(() => Submitted().Submit(8, 8, null, Now)).ErrorCode.ShouldBe("TIMESHEET_STATE");

    [Fact]
    public void RejectedHours_CanBeSubmittedAgain()
    {
        TimesheetApproval share = Submitted();
        share.Reject(Now);
        share.Status.ShouldBe(ApprovalStatus.Rejected);
        share.IsLocked.ShouldBeFalse();

        share.Submit(6, 6, null, Now);

        share.Status.ShouldBe(ApprovalStatus.Submitted);
        share.Hours.ShouldBe(6);
        share.DecidedDate.ShouldBeNull();
    }

    [Fact]
    public void OnlySubmittedHours_AreApproved_AndOnlyApprovedHours_Reopened()
    {
        Should.Throw<DomainRuleException>(() => NewShare().Approve(Now));
        Should.Throw<DomainRuleException>(() => Submitted().Reopen());
    }

    [Fact]
    public void ReturningHours_LeavesADecisionAlone()
    {
        TimesheetApproval share = Submitted();
        share.Approve(Now);

        share.Return();

        share.Status.ShouldBe(ApprovalStatus.Approved);
    }
}
