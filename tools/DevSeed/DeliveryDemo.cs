using Gggs.Data;
using Gggs.Modules.Client.Domain;
using Gggs.Modules.Contract.Domain;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Project.Application;
using Gggs.Modules.Project.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ClientEntity = Gggs.Modules.Client.Domain.Client;
using ContractEntity = Gggs.Modules.Contract.Domain.Contract;
using ProjectEntity = Gggs.Modules.Project.Domain.Project;

namespace Gggs.DevSeed;

/// <summary>
/// Development-only delivery data (M5): the project roles every tenant gets, then a client with a
/// signed MSA and SOW and one running project - team, board, a sprint in flight, a milestone, a
/// risk, an issue and a change request - so the project screens have something real to show.
/// </summary>
internal static class DeliveryDemo
{
    public static async Task SeedAsync(IServiceProvider scope)
    {
        int roles = await scope.GetRequiredService<ProjectCatalogSeeder>().SeedCurrentTenantAsync();
        AppDbContext db = scope.GetRequiredService<AppDbContext>();

        if (await db.Set<ClientEntity>().AnyAsync())
        {
            Console.WriteLine($"Project roles: {roles} added. Demo client already present.");
            return;
        }

        IReadOnlyDictionary<string, Guid> people = await scope.GetRequiredService<IEmployeeDirectory>()
            .ResolveCodesAsync(["EMP00001", "EMP00002", "EMP00003", "EMP00004", "EMP00005", "EMP00006", "EMP00007"]);
        if (people.Count < 7)
        {
            Console.WriteLine("Demo employees missing; skipping the demo project.");
            return;
        }

        Guid anita = people["EMP00001"];
        Guid vikram = people["EMP00002"];
        Guid meera = people["EMP00003"];
        Guid arjun = people["EMP00004"];
        Guid sara = people["EMP00005"];
        Guid rohan = people["EMP00006"];
        Guid priya = people["EMP00007"];

        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        DateTime now = DateTime.UtcNow;

        var client = ClientEntity.Create("NORTHWIND", new ClientProfile(
            "Northwind Retail", "Northwind Retail Private Limited", "RETAIL", "IN", null, "Mumbai", "Lower Parel", "400013",
            "https://northwind.example", "accounts@northwind.example", "+91 22 4000 1000", "27AAACN1234F1Z5", anita, null), ClientStatus.Active);
        db.Add(client);
        var contact = ClientContact.Create(client.Id, new ContactProfile("Asha Rao", "Chief digital officer", "asha.rao@northwind.example", "+91 98200 00001", "Digital", null));
        contact.MakePrimary();
        db.Add(contact);
        db.Add(ClientContact.Create(client.Id, new ContactProfile("Kunal Mehta", "Procurement lead", "kunal.mehta@northwind.example", null, "Procurement", null)));

        var msa = ContractEntity.Draft($"CTR-{today.Year}-0001", client.Id, new ContractTerms(
            "Master services agreement", ContractType.Msa, null, today.AddMonths(-6), today.AddYears(2), "INR", null, BillingModel.TimeAndMaterial,
            45, "45 days from invoice", "GST 18% extra", today.AddYears(2).AddMonths(-2), false, anita, contact.Id, null));
        msa.Activate(now, today);
        var sow = ContractEntity.Draft($"CTR-{today.Year}-0002", client.Id, new ContractTerms(
            "SOW 1 - commerce platform rebuild", ContractType.Sow, msa.Id, today.AddMonths(-2), today.AddMonths(4), "INR", 18_000_000m,
            BillingModel.TimeAndMaterial, 45, null, null, today.AddMonths(3), false, vikram, contact.Id, null));
        sow.Activate(now, today);
        db.AddRange(msa, sow);

        var project = ProjectEntity.Create("NWCOM", new ProjectProfile(
            "Northwind commerce platform", ProjectType.TimeAndMaterial, "Digital commerce", client.Id, sow.Id, null, null, vikram, anita, anita,
            today.AddMonths(-2), today.AddMonths(4), null, WorkPriority.High, ProjectBillingType.TimeAndMaterial, "INR", 18_000_000m, 18_000_000m,
            11_000_000m, "Rebuild of Northwind's web and mobile commerce on a headless platform.", null, "HYBRID", Confidentiality.Confidential,
            DeliveryMethodology.Agile), ProjectStage.Execution);
        project.RateClientSatisfaction(4, today.AddDays(-7));
        db.Add(project);

        List<BoardColumn> columns = [];
        int order = 0;
        foreach ((string name, ColumnCategory category) in BoardColumn.Defaults)
        {
            columns.Add(BoardColumn.Create(project.Id, name, category, order++, category == ColumnCategory.InProgress ? 5 : null));
        }

        db.AddRange(columns);
        BoardColumn Column(string name) => columns.First(column => column.Name == name);

        Dictionary<string, Guid> roleIds = await db.Set<ProjectRole>().ToDictionaryAsync(role => role.Code, role => role.Id);
        void Member(Guid employee, string role, int allocation, decimal rate, decimal cost)
            => db.Add(ProjectTeamMember.Add(project.Id, employee, new TeamAssignment(roleIds[role], today.AddMonths(-2), today.AddMonths(4), allocation, true, rate, cost, null)));

        Member(vikram, "PM", 50, 4500, 2200);
        Member(meera, "TL", 100, 3800, 1800);
        Member(arjun, "DEV", 100, 2800, 1200);
        Member(sara, "DEV", 100, 2600, 1100);
        Member(rohan, "QA", 80, 2400, 1000);

        ProjectPhase discovery = ProjectPhase.Create(project.Id, new PhasePlan("Discovery", 0, today.AddMonths(-2), today.AddMonths(-1), vikram, 2_000_000m, PhaseStatus.Completed, 100));
        ProjectPhase build = ProjectPhase.Create(project.Id, new PhasePlan("Build", 1, today.AddMonths(-1), today.AddMonths(3), meera, 13_000_000m, PhaseStatus.InProgress, 40));
        ProjectPhase launch = ProjectPhase.Create(project.Id, new PhasePlan("Launch", 2, today.AddMonths(3), today.AddMonths(4), vikram, 3_000_000m, PhaseStatus.NotStarted, 0));
        db.AddRange(discovery, build, launch);

        ProjectMilestone design = ProjectMilestone.Create(project.Id, new MilestonePlan("Design sign-off", null, today.AddMonths(-1), vikram, discovery.Id, MilestoneStatus.Planned, 100, true));
        design.Achieve(today.AddMonths(-1).AddDays(2));
        ProjectMilestone beta = ProjectMilestone.Create(project.Id, new MilestonePlan("Checkout beta", "Card and UPI checkout on staging", today.AddDays(21), meera, build.Id, MilestoneStatus.InProgress, 30, true));
        ProjectMilestone golive = ProjectMilestone.Create(project.Id, new MilestonePlan("Go-live", null, today.AddMonths(4).AddDays(-7), vikram, launch.Id, MilestoneStatus.Planned, 0, true));
        db.AddRange(design, beta, golive);

        Sprint sprint = Sprint.Plan(project.Id, "Sprint 4", today.AddDays(-4), today.AddDays(9), "Card checkout end to end", 34);

        int rank = 1000;
        ProjectTask NewTask(TaskType type, string title, ProjectTask? parent, Guid? assignee, int? points, string column, bool inSprint, WorkPriority priority = WorkPriority.Medium)
        {
            var task = ProjectTask.Create(project.Id, project.NextTaskNumber(), project.Code, new TaskDetails(
                type, parent?.Id, build.Id, type == TaskType.Epic ? null : beta.Id, null, title, null, assignee, priority, null, today.AddDays(inSprint ? 9 : 30),
                points is null ? null : points * 6m, null, points, 0, true), Column(column), rank += 1000, vikram, now.AddDays(-1));
            if (inSprint)
            {
                task.PlanInto(sprint.Id);
            }

            db.Add(task);
            return task;
        }

        ProjectTask checkout = NewTask(TaskType.Epic, "Checkout", null, meera, null, "Backlog", false);
        ProjectTask card = NewTask(TaskType.Story, "Pay by card", checkout, arjun, 8, "In Progress", true, WorkPriority.High);
        NewTask(TaskType.Story, "Saved addresses", checkout, sara, 5, "Done", true);
        NewTask(TaskType.Story, "Order summary page", checkout, sara, 3, "Code Review", true);
        NewTask(TaskType.Story, "Pay by UPI", checkout, arjun, 8, "To Do", true);
        NewTask(TaskType.Task, "Payment gateway sandbox keys", card, meera, null, "Done", true);
        NewTask(TaskType.Bug, "Cart total ignores coupon", checkout, sara, 2, "Testing", true, WorkPriority.High);
        NewTask(TaskType.Story, "Wishlist", null, null, 5, "Backlog", false, WorkPriority.Low);
        NewTask(TaskType.Story, "Order tracking", null, null, 8, "Backlog", false);

        sprint.Start(29, now.AddDays(-4));
        db.Add(sprint);

        db.Add(RaidItem.Create(project.Id, project.NextRaidNumber(), RaidKind.Risk, new RaidDetails(
            "Gateway certification may slip past beta", "PCI certification lead time is 3-4 weeks.", "Third party", 3, 5, null, WorkPriority.High, vikram, meera,
            today.AddDays(14), "Start certification paperwork this sprint", "Launch with UPI only if cards are late", null, null, RaidStatus.Open), today));
        db.Add(RaidItem.Create(project.Id, project.NextRaidNumber(), RaidKind.Issue, new RaidDetails(
            "Staging database refresh is failing", null, "Environment", null, null, Severity.Medium, WorkPriority.Medium, meera, rohan,
            today.AddDays(3), null, null, "Snapshot job lost its credentials", null, RaidStatus.InProgress), today));
        db.Add(RaidItem.Create(project.Id, project.NextRaidNumber(), RaidKind.Assumption, new RaidDetails(
            "Client provides product data in the agreed CSV format", null, "Data", null, null, null, WorkPriority.Medium, vikram, null,
            null, null, null, null, null, RaidStatus.Open), today));

        db.Add(ChangeRequest.Draft(project.Id, project.NextChangeRequestNumber(), new ChangeRequestDetails(
            "Add gift cards", "Customers redeem Northwind gift cards at checkout.", "Requested by the client's marketing team",
            "Payments service, checkout UI, reconciliation report", 160, 600_000m, 10, WorkPriority.Medium, null, "Asha Rao (Northwind)")));

        ProjectMeeting standup = ProjectMeeting.Schedule(project.Id, new MeetingDetails(
            "Sprint 4 planning", MeetingType.SprintPlanning, today.AddDays(-4), new TimeOnly(10, 0), 60, "Teams", [vikram, meera, arjun, sara, rohan], null,
            "Sprint goal, capacity, commitments", "Committed 29 points. UPI story stretch goal.", vikram, MeetingStatus.Held));
        db.Add(standup);
        db.Add(MeetingAction.Create(project.Id, standup.Id, "Share gateway certification checklist with the client", vikram, today.AddDays(2)));
        db.Add(ProjectMeeting.Schedule(project.Id, new MeetingDetails(
            "Weekly status with Northwind", MeetingType.Client, today.AddDays(2), new TimeOnly(16, 0), 45, "Northwind office, Lower Parel", [vikram, anita], "Asha Rao, Kunal Mehta",
            "Progress, beta readiness, gift card CR", null, vikram, MeetingStatus.Scheduled)));

        db.Add(ProjectStatusHistory.Record(project.Id, "Stage", "-", ProjectStage.Execution.ToString(), "Project created", priya, now));
        await db.SaveChangesAsync();
        Console.WriteLine($"Project roles: {roles} added. Demo client Northwind Retail with MSA, SOW and project NWCOM (5 people, 9 tasks, sprint running).");
    }
}
