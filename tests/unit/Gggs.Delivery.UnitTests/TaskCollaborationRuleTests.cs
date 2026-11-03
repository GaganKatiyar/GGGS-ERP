using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Modules.Project.Domain;
using Shouldly;

namespace Gggs.Delivery.UnitTests;

public sealed class TaskCollaborationRuleTests
{
    // ----- What changed ---------------------------------------------------------------------------

    private static readonly Guid Backlog = Guid.NewGuid();

    private static TaskState State() => new(TaskType.Task, "Build it", null, WorkPriority.Medium, Backlog, null, null, null, null, null);

    [Fact]
    public void ASaveThatChangedNothing_RecordsNothing()
        => TaskFields.Changed(State(), State()).ShouldBeEmpty();

    [Fact]
    public void EachChange_IsNamed()
    {
        TaskState after = State() with
        {
            Type = TaskType.Bug,
            Title = "Fix it",
            AssigneeEmployeeId = Guid.NewGuid(),
            Priority = WorkPriority.High,
            ColumnId = Guid.NewGuid(),
            SprintId = Guid.NewGuid(),
            StartDate = new DateOnly(2026, 11, 2),
            DueDate = new DateOnly(2026, 11, 6),
            EstimatedHours = 8,
            StoryPoints = 3,
        };

        TaskFields.Changed(State(), after).ShouldBe([
            TaskFields.Type, TaskFields.Title, TaskFields.Assignee, TaskFields.Priority, TaskFields.Column, TaskFields.Sprint,
            TaskFields.StartDate, TaskFields.DueDate, TaskFields.Estimate, TaskFields.Points]);
    }

    // ----- Comments and views ---------------------------------------------------------------------

    [Fact]
    public void AComment_NeedsSomethingToSay_AndHasALimit()
    {
        Should.Throw<Exception>(() => TaskComment.Post(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "   ", DateTime.UtcNow));
        Should.Throw<Exception>(() => TaskComment.Post(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new string('x', 4001), DateTime.UtcNow));
        TaskComment.Post(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "  Looks good  ", DateTime.UtcNow).Body.ShouldBe("Looks good");
    }

    [Fact]
    public void AView_ReadsBackItsFilters_AndHasNoOwnerWhenShared()
    {
        Guid person = Guid.NewGuid();
        TaskSavedView view = TaskSavedView.Save(Guid.NewGuid(), null, "My bugs", new TaskViewFilters(Type: TaskType.Bug, AssigneeEmployeeId: person, Overdue: true));

        view.IsShared.ShouldBeTrue();
        view.Filters.Type.ShouldBe(TaskType.Bug);
        view.Filters.AssigneeEmployeeId.ShouldBe(person);
        view.Filters.Overdue.ShouldBe(true);
        view.Filters.Priority.ShouldBeNull();
    }

    [Theory]
    [InlineData(TaskType.Experiment, null, true)]
    [InlineData(TaskType.Experiment, TaskType.Story, true)]
    [InlineData(TaskType.Subtask, TaskType.Experiment, true)]
    [InlineData(TaskType.Experiment, TaskType.Task, false)]
    public void AnExperiment_SitsInTheHierarchyLikeATask(TaskType child, TaskType? parent, bool allowed)
        => TaskHierarchy.CanHaveParent(child, parent).ShouldBe(allowed);

    // ----- Workload -------------------------------------------------------------------------------

    // 2 November 2026 is a Monday.
    private static readonly DateOnly Monday = new(2026, 11, 2);

    private static Workload.Assignment Full => new(100, new DateOnly(2026, 1, 1), null);

    private static Workload.OpenTask Task(DateOnly? due, decimal? estimate, decimal? actual = null, DateOnly? start = null) => new(start, due, estimate, actual);

    [Fact]
    public void ThePeopleOnTheProject_HaveTheirAllocationAsCapacity()
    {
        Workload.Person full = Workload.For([Full], [], Monday, 2);
        full.Weeks[0].CapacityHours.ShouldBe(40);

        Workload.Person half = Workload.For([new Workload.Assignment(50, new DateOnly(2026, 1, 1), null)], [], Monday, 1);
        half.Weeks[0].CapacityHours.ShouldBe(20);
    }

    [Fact]
    public void ASeatEndingMidweek_HasOnlyThoseDays()
    {
        Workload.Person person = Workload.For([new Workload.Assignment(100, new DateOnly(2026, 1, 1), Monday.AddDays(2))], [], Monday, 2);

        person.Weeks[0].CapacityHours.ShouldBe(24);
        person.Weeks[1].CapacityHours.ShouldBe(0);
    }

    [Fact]
    public void Work_SpreadsOverTheWorkingDaysToItsDueDate()
    {
        // Ten working days from Monday to the Friday after, so forty hours is four a day.
        Workload.Person person = Workload.For([Full], [Task(Monday.AddDays(11), 40)], Monday, 3);

        person.Weeks[0].PlannedHours.ShouldBe(20);
        person.Weeks[1].PlannedHours.ShouldBe(20);
        person.Weeks[2].PlannedHours.ShouldBe(0);
        person.RemainingHours.ShouldBe(40);
    }

    [Fact]
    public void WhatHasBeenDone_ComesOffTheEstimate()
        => Workload.For([Full], [Task(Monday.AddDays(4), 10, actual: 4)], Monday, 1).Weeks[0].PlannedHours.ShouldBe(6);

    [Fact]
    public void WorkThatStartsLater_WaitsUntilThen()
    {
        Workload.Person person = Workload.For([Full], [Task(Monday.AddDays(11), 10, start: Monday.AddDays(7))], Monday, 2);

        person.Weeks[0].PlannedHours.ShouldBe(0);
        person.Weeks[1].PlannedHours.ShouldBe(10);
    }

    [Fact]
    public void LateWork_LandsNow_AndCountsAsOverdue()
    {
        Workload.Person person = Workload.For([Full], [Task(Monday.AddDays(-3), 12)], Monday, 2);

        person.Overdue.ShouldBe(1);
        person.Weeks[0].PlannedHours.ShouldBe(12);
    }

    [Fact]
    public void WorkWithNoDueDate_HasNoWeek_AndWorkWithNoEstimate_IsCounted()
    {
        Workload.Person person = Workload.For([Full], [Task(null, 8), Task(Monday.AddDays(1), null)], Monday, 1);

        person.UnscheduledHours.ShouldBe(8);
        person.Unestimated.ShouldBe(1);
        person.OpenTasks.ShouldBe(2);
        person.Weeks[0].PlannedHours.ShouldBe(0);
    }

    [Fact]
    public void OnAWeekend_LateWorkWaitsForMonday()
    {
        Workload.Person person = Workload.For([Full], [Task(Monday.AddDays(-3), 8)], Monday.AddDays(-2), 1);

        person.Weeks[0].WeekStart.ShouldBe(Monday.AddDays(-7));
        person.Weeks[0].PlannedHours.ShouldBe(0, "Monday is in the next week");
    }

    // ----- Reading a file -------------------------------------------------------------------------

    private static readonly ImportPerson Asha = new(Guid.NewGuid(), "Asha Rao", "asha@acme.example", "E100");

    private static readonly ImportColumn[] Board =
    [
        new(Guid.NewGuid(), "Backlog", ColumnCategory.Backlog),
        new(Guid.NewGuid(), "To Do", ColumnCategory.Todo),
        new(Guid.NewGuid(), "In Progress", ColumnCategory.InProgress),
        new(Guid.NewGuid(), "Done", ColumnCategory.Done),
    ];

    private static IReadOnlyList<ImportRow> Plan(string csv, params ImportExisting[] existing) => TaskImport.Plan(csv, [Asha], Board, existing);

    [Fact]
    public void Csv_HandlesQuotesCommasNewlinesAndAByteOrderMark()
    {
        IReadOnlyList<IReadOnlyList<string>> rows = TaskImport.ReadCsv("﻿Title,Notes\r\n\"Fix, then ship\",\"He said \"\"now\"\"\nand left\"\n\nLast,\n");

        rows.Count.ShouldBe(3);
        rows[0][0].ShouldBe("Title");
        rows[1][0].ShouldBe("Fix, then ship");
        rows[1][1].ShouldBe("He said \"now\"\nand left");
        rows[2].ShouldBe(["Last", string.Empty]);
    }

    [Fact]
    public void ASimpleFile_BecomesTasksInTheBacklog()
    {
        ImportRow row = Plan("Title,Priority,Assignee,Due date,Estimate\nBuild it,High,asha@acme.example,2026-12-01,8").Single();

        row.Title.ShouldBe("Build it");
        row.Type.ShouldBe(TaskType.Task);
        row.Priority.ShouldBe(WorkPriority.High);
        row.AssigneeId.ShouldBe(Asha.Id);
        row.DueDate.ShouldBe(new DateOnly(2026, 12, 1));
        row.EstimatedHours.ShouldBe(8);
        row.ColumnName.ShouldBe("Backlog");
        row.Warnings.ShouldBeEmpty();
        row.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void AJiraExport_IsUnderstood()
    {
        const string Csv = "Summary,Issue key,Issue id,Issue Type,Status,Priority,Assignee,Due date,Custom field (Story Points),Original Estimate,Parent id\n"
            + "Checkout,AP-1,1001,Epic,To Do,Highest,,,,,\n"
            + "Pay by card,AP-2,1002,Story,In Progress,Major,E100,14/Dec/26 5:00 PM,5,28800,1001\n"
            + "Refund bug,AP-3,1003,Bug,Closed,Minor,,,,,1001\n";

        IReadOnlyList<ImportRow> rows = Plan(Csv);

        rows[0].Type.ShouldBe(TaskType.Epic);
        rows[0].Priority.ShouldBe(WorkPriority.Critical);
        rows[0].ColumnName.ShouldBe("To Do");
        rows[1].Type.ShouldBe(TaskType.Story);
        rows[1].ParentKey.ShouldBe("AP-1");
        rows[1].AssigneeId.ShouldBe(Asha.Id);
        rows[1].DueDate.ShouldBe(new DateOnly(2026, 12, 14));
        rows[1].StoryPoints.ShouldBe(5);
        rows[1].EstimatedHours.ShouldBe(8, "Jira gives seconds");
        rows[1].ColumnName.ShouldBe("In Progress");
        rows[2].ColumnName.ShouldBe("Done");
        rows[2].Priority.ShouldBe(WorkPriority.Low);
        rows[1].Depth.ShouldBe(1);
        rows[0].Depth.ShouldBe(0);
    }

    [Fact]
    public void ThingsThatDoNotFit_AreChangedAndReported()
    {
        ImportRow row = Plan("Title,Type,Priority,Assignee,Status,Due date,Story Points\nBuild it,Wish,Soon,Nobody,Limbo,sometime,many").Single();

        row.Type.ShouldBe(TaskType.Task);
        row.Priority.ShouldBe(WorkPriority.Medium);
        row.AssigneeId.ShouldBeNull();
        row.ColumnName.ShouldBe("Backlog");
        row.DueDate.ShouldBeNull();
        row.StoryPoints.ShouldBeNull();
        row.Warnings.Count.ShouldBe(6);
        row.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void ARowWithNoTitle_IsAnError_AndARepeatedKeyToo()
    {
        IReadOnlyList<ImportRow> rows = Plan("Title,Key\n,A-1\nFirst,A-2\nSecond,A-2");

        rows[0].Errors.ShouldNotBeEmpty();
        rows[1].Errors.ShouldBeEmpty();
        rows[2].Errors.ShouldNotBeEmpty();
    }

    [Fact]
    public void ThePreviouslyImported_AreMarked_ButATaskThatOnlySharesACodeIsNot()
    {
        IReadOnlyList<ImportRow> rows = Plan("Title,Key\nOld,JIRA-1\nNew,JIRA-2\nOther,APOLLO-9", new ImportExisting(Guid.NewGuid(), "APOLLO-1", "JIRA-1", TaskType.Task), new ImportExisting(Guid.NewGuid(), "APOLLO-9", null, TaskType.Task));

        rows[0].AlreadyImported.ShouldBeTrue();
        rows[1].AlreadyImported.ShouldBeFalse();
        rows[2].AlreadyImported.ShouldBeFalse();
    }

    [Fact]
    public void AParent_CanBeInTheFile_OrAlreadyInTheProject_OrMissing()
    {
        IReadOnlyList<ImportRow> rows = Plan(
            "Title,Key,Type,Parent\nChild,C-1,Story,E-9\nOther,C-2,Story,APOLLO-1\nOrphan,C-3,Story,X-404\nEpic,E-9,Epic,",
            new ImportExisting(Guid.NewGuid(), "APOLLO-1", null, TaskType.Epic));

        rows[0].ParentKey.ShouldBe("E-9");
        rows[0].Depth.ShouldBe(1);
        rows[3].Depth.ShouldBe(0, "a parent goes in before its children, wherever it is in the file");
        rows[1].ParentKey.ShouldBe("APOLLO-1");
        rows[2].ParentKey.ShouldBeNull();
        rows[2].Warnings.ShouldHaveSingleItem();
    }

    [Fact]
    public void ASubtaskWithNoParent_ComesInAsATask()
    {
        ImportRow row = Plan("Title,Type\nLoose,Sub-task").Single();

        row.Type.ShouldBe(TaskType.Task);
        row.Warnings.ShouldHaveSingleItem();
    }

    [Fact]
    public void AParentThatIsNotAllowed_IsDropped()
    {
        IReadOnlyList<ImportRow> rows = Plan("Title,Key,Type,Parent\nOne,A,Story,\nTwo,B,Story,A");

        rows[1].ParentKey.ShouldBeNull();
        rows[1].Type.ShouldBe(TaskType.Story);
        rows[1].Warnings.ShouldHaveSingleItem();
    }

    [Fact]
    public void ParentsThatLoop_DoNotHangTheImport()
    {
        IReadOnlyList<ImportRow> rows = Plan("Title,Key,Type,Parent\nOne,A,Task,B\nTwo,B,Task,A");

        rows.Count.ShouldBe(2);
        rows.ShouldAllBe(row => row.ParentKey == null);
    }

    [Fact]
    public void LongText_IsShortened_AndDatesTheWrongWayRoundAreFixed()
    {
        ImportRow row = Plan($"Title,Description,Start date,Due date\n{new string('t', 310)},{new string('d', 8100)},2026-12-10,2026-12-01").Single();

        row.Title.Length.ShouldBe(300);
        row.Description!.Length.ShouldBe(8000);
        row.StartDate.ShouldBeNull();
        row.DueDate.ShouldBe(new DateOnly(2026, 12, 1));
        row.Warnings.Count.ShouldBe(3);
    }

    [Fact]
    public void AnEstimateOutOfRange_IsLeftOut()
        => Plan("Title,Estimate\nBuild it,99999").Single().EstimatedHours.ShouldBeNull();

    [Fact]
    public void AFileWithNoTitleColumn_OrNothingInIt_IsRefused()
    {
        Should.Throw<DomainRuleException>(() => Plan("Name of thing,Other\nA,B".Replace("Name of thing", "Thing", StringComparison.Ordinal))).ErrorCode.ShouldBe("TASK_IMPORT_COLUMNS");
        Should.Throw<DomainRuleException>(() => Plan("   \n")).ErrorCode.ShouldBe("TASK_IMPORT_COLUMNS");
        Should.Throw<DomainRuleException>(() => Plan(string.Empty)).ErrorCode.ShouldBe("TASK_IMPORT_EMPTY");
    }

    [Fact]
    public void TooManyRows_OrTooBigAFile_IsRefused()
    {
        string many = "Title\n" + string.Concat(Enumerable.Range(0, 1001).Select(index => $"Task {index}\n"));
        Should.Throw<DomainRuleException>(() => Plan(many)).ErrorCode.ShouldBe("TASK_IMPORT_SIZE");
        Should.Throw<DomainRuleException>(() => Plan(new string('x', TaskImport.MaxCharacters + 1))).ErrorCode.ShouldBe("TASK_IMPORT_SIZE");
    }

    // ----- Dates ----------------------------------------------------------------------------------

    private static ProjectTask ATask()
    {
        Guid project = Guid.NewGuid();
        BoardColumn column = BoardColumn.Create(project, "To Do", ColumnCategory.Todo, 0, null);
        var details = new TaskDetails(TaskType.Task, null, null, null, null, "Build it", null, null, WorkPriority.Medium, Monday, Monday.AddDays(4), null, null, null, 0, true);
        return ProjectTask.Create(project, 1, "P1", details, column, 1000, null, DateTime.UtcNow);
    }

    [Fact]
    public void Rescheduling_MovesTheDates_ButNotBackToFront()
    {
        ProjectTask task = ATask();

        task.Reschedule(Monday.AddDays(7), Monday.AddDays(9));
        task.StartDate.ShouldBe(Monday.AddDays(7));
        task.DueDate.ShouldBe(Monday.AddDays(9));

        Should.Throw<DomainRuleException>(() => task.Reschedule(Monday.AddDays(9), Monday)).ErrorCode.ShouldBe("PROJECT_TASK_DATES");
    }

    [Fact]
    public void TheBaseline_KeepsTheDatesItWasTakenWith()
    {
        ProjectTask task = ATask();
        task.CaptureBaseline();

        task.Reschedule(Monday.AddDays(7), Monday.AddDays(9));

        task.BaselineStartDate.ShouldBe(Monday);
        task.BaselineDueDate.ShouldBe(Monday.AddDays(4));
    }

    [Fact]
    public void AnImportedKey_IsKept_AndABlankOneIsNot()
    {
        ProjectTask task = ATask();

        task.MarkImported("  JIRA-1 ");
        task.ExternalKey.ShouldBe("JIRA-1");

        task.MarkImported(" ");
        task.ExternalKey.ShouldBeNull();
    }
}
