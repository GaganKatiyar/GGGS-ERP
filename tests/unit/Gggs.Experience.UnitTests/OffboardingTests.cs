using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Modules.Separation.Domain;
using Shouldly;

namespace Gggs.Experience.UnitTests;

/// <summary>Separation v2 phase B: offboarding templates and tasks, handover items, the successor.</summary>
public sealed class OffboardingTests
{
    private static readonly DateOnly LastDay = new(2026, 11, 30);

    private static OffboardingTemplateDetails Details(int offset = -5, bool isDefault = false, Guid? department = null, string? type = null)
        => new("Checklist", null, department, null, type, isDefault, [new OffboardingStep("Return laptop", null, OffboardingOwner.Admin, offset)]);

    [Fact]
    public void The_standard_checklist_covers_every_owner_but_the_team_queues_optional_finance()
    {
        OffboardingTemplate standard = OffboardingTemplate.Standard();
        standard.IsDefault.ShouldBeTrue();
        standard.Steps.Select(step => step.Owner).Distinct().Count().ShouldBe(7);
        standard.Steps.Select(step => step.DueOffsetDays).ShouldBeInOrder();
        standard.Steps.Single(step => step.Owner == OffboardingOwner.Finance).Required.ShouldBeFalse();
    }

    [Fact]
    public void Templates_reject_steps_far_after_the_last_day_and_a_scoped_default()
    {
        Should.Throw<DomainRuleException>(() => OffboardingTemplate.Create(Details(offset: 31))).ErrorCode.ShouldBe("OFFBOARDING_STEPS");
        Should.Throw<DomainRuleException>(() => OffboardingTemplate.Create(Details(isDefault: true, type: "CONTRACT"))).ErrorCode.ShouldBe("OFFBOARDING_DEFAULT");
        Should.Throw<DomainRuleException>(() => OffboardingTemplate.Create(Details() with { Steps = [] })).ErrorCode.ShouldBe("OFFBOARDING_STEPS");
    }

    [Fact]
    public void The_more_specific_template_fits_better_and_a_foreign_one_not_at_all()
    {
        Guid sales = Guid.NewGuid();
        OffboardingTemplate general = OffboardingTemplate.Create(Details(isDefault: true));
        OffboardingTemplate salesTemplate = OffboardingTemplate.Create(Details(department: sales, type: "full_time"));

        general.Fit(sales, null, "FULL_TIME").ShouldBe(0);
        salesTemplate.Fit(sales, null, "FULL_TIME").ShouldBe(2);
        salesTemplate.Fit(Guid.NewGuid(), null, "FULL_TIME").ShouldBe(-1);
        salesTemplate.EmploymentTypeCode.ShouldBe("FULL_TIME");

        salesTemplate.Update(Details(department: sales), active: false);
        salesTemplate.Fit(sales, null, null).ShouldBe(-1, "an inactive template never applies");
    }

    [Fact]
    public void Tasks_fall_due_from_the_last_day_and_follow_it_while_open()
    {
        var step = new OffboardingStep("Hand over", null, OffboardingOwner.Employee, -5);
        OffboardingTask task = OffboardingTask.From(Guid.NewGuid(), step, LastDay, Guid.NewGuid(), 0);
        task.DueDate.ShouldBe(new DateOnly(2026, 11, 25));

        task.Follow(new DateOnly(2026, 12, 15));
        task.DueDate.ShouldBe(new DateOnly(2026, 12, 10));

        task.Finish(skipped: false, null, " done ", DateTime.UtcNow);
        task.Notes.ShouldBe("done");
        task.Follow(LastDay);
        task.DueDate.ShouldBe(new DateOnly(2026, 12, 10), "a closed task keeps its date");
        Should.Throw<DomainRuleException>(() => task.Finish(skipped: false, null, null, DateTime.UtcNow)).ErrorCode.ShouldBe("OFFBOARDING_TASK_STATE");
    }

    [Fact]
    public void Required_tasks_cannot_be_skipped_and_overdue_reminders_wait_three_days()
    {
        OffboardingTask required = OffboardingTask.From(Guid.NewGuid(), new OffboardingStep("Collect laptop", null, OffboardingOwner.Admin, 0), LastDay, Guid.NewGuid(), 0);
        Should.Throw<DomainRuleException>(() => required.Finish(skipped: true, null, null, DateTime.UtcNow)).ErrorCode.ShouldBe("OFFBOARDING_TASK_REQUIRED");

        DateOnly later = LastDay.AddDays(2);
        required.NeedsReminder(later).ShouldBeTrue();
        required.Reminded(later);
        required.NeedsReminder(later.AddDays(2)).ShouldBeFalse();
        required.NeedsReminder(later.AddDays(3)).ShouldBeTrue();

        OffboardingTask optional = OffboardingTask.From(Guid.NewGuid(), new OffboardingStep("Close card", null, OffboardingOwner.Finance, 0, Required: false), LastDay, null, 1);
        optional.NeedsReminder(later).ShouldBeFalse("a team-queue task has nobody to remind");
        optional.Finish(skipped: true, null, null, DateTime.UtcNow);
        optional.Status.ShouldBe(OffboardingTaskStatus.Skipped);
    }

    [Fact]
    public void Handover_items_close_once_and_only_open_ones_change()
    {
        Guid successor = Guid.NewGuid();
        HandoverItem seat = HandoverItem.Raise(Guid.NewGuid(), HandoverKind.ProjectAllocation, Guid.NewGuid(), "Release from P1", null, null);
        seat.NeedsRecipient.ShouldBeFalse();

        HandoverItem tasks = HandoverItem.Raise(Guid.NewGuid(), HandoverKind.ProjectTasks, null, "Hand over 2 open project task(s)", null, null);
        tasks.NeedsRecipient.ShouldBeTrue();
        tasks.Target(successor);
        tasks.Refresh("Hand over 3 open project task(s)", "P1-4; P1-5; P1-6");
        tasks.Close(skipped: false, null, "3 task(s) moved", DateTime.UtcNow);
        tasks.Refresh("Hand over 9 open project task(s)", null);
        tasks.Title.ShouldBe("Hand over 3 open project task(s)");
        tasks.ToEmployeeId.ShouldBe(successor);
        Should.Throw<DomainRuleException>(() => tasks.Close(skipped: true, null, null, DateTime.UtcNow)).ErrorCode.ShouldBe("HANDOVER_STATE");
    }

    [Fact]
    public void A_leaver_cannot_succeed_themselves()
    {
        Guid employee = Guid.NewGuid();
        SeparationCase separation = SeparationCase.Open(employee, SeparationType.Resignation, "Moving on", new DateOnly(2026, 10, 1), 60, LastDay);
        Should.Throw<DomainRuleException>(() => separation.SetHandover(employee, null)).ErrorCode.ShouldBe("SEPARATION_SUCCESSOR");

        Guid successor = Guid.NewGuid();
        separation.SetHandover(successor, "  Owns the billing service  ");
        separation.SuccessorEmployeeId.ShouldBe(successor);
        separation.HandoverNote.ShouldBe("Owns the billing service");

        var first = new DateTime(2026, 11, 30, 18, 0, 0, DateTimeKind.Utc);
        separation.AccessDisabled(first);
        separation.AccessDisabled(first.AddDays(1));
        separation.AccessDisabledAt.ShouldBe(first);
    }
}
