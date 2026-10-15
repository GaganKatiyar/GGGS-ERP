using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Project.Domain;
using Gggs.Modules.Resources.Domain;
using Gggs.Modules.Skills.Application;
using Gggs.Modules.Skills.Domain;
using Gggs.Modules.Timesheets.Contracts;
using Gggs.Modules.Timesheets.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectEntity = Gggs.Modules.Project.Domain.Project;

namespace Gggs.DevSeed;

/// <summary>
/// Development-only resourcing data (M6): the skill catalog and the demo people's skills, resource
/// profiles with rates, an open request on NWCOM, and approved timesheets for the last two weeks so
/// utilization has something to measure.
/// </summary>
internal static class ResourceDemo
{
    public static async Task SeedAsync(IServiceProvider scope)
    {
        int catalog = await scope.GetRequiredService<SkillCatalogSeeder>().SeedCurrentTenantAsync();
        AppDbContext db = scope.GetRequiredService<AppDbContext>();

        if (await db.Set<ResourceProfile>().AnyAsync())
        {
            Console.WriteLine($"Skills: {catalog} added. Demo resourcing already present.");
            return;
        }

        IReadOnlyDictionary<string, Guid> people = await scope.GetRequiredService<IEmployeeDirectory>()
            .ResolveCodesAsync(["EMP00001", "EMP00002", "EMP00003", "EMP00004", "EMP00005", "EMP00006", "EMP00007"]);
        ProjectEntity? project = await db.Set<ProjectEntity>().FirstOrDefaultAsync(row => row.Code == "NWCOM");
        if (people.Count < 7 || project is null)
        {
            Console.WriteLine($"Skills: {catalog} added. Demo people or project missing; skipping demo resourcing.");
            return;
        }

        Dictionary<string, Guid> skills = await db.Set<Skill>().ToDictionaryAsync(skill => skill.Code, skill => skill.Id);
        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);

        void Skill(string employee, string skill, int level, int? target, decimal years, bool primary)
        {
            var row = EmployeeSkill.Record(people[employee], skills[skill]);
            row.Assess(level, target, years, primary, AssessmentSource.Manager, people["EMP00002"], today.AddDays(-30));
            db.Add(row);
        }

        Skill("EMP00001", "ARCHITECTURE", 5, null, 15, true);
        Skill("EMP00001", "AZURE", 4, null, 8, false);
        Skill("EMP00002", "PM", 4, 5, 6, true);
        Skill("EMP00002", "DOTNET", 4, null, 10, false);
        Skill("EMP00003", "DOTNET", 5, null, 9, true);
        Skill("EMP00003", "ANGULAR", 4, null, 6, true);
        Skill("EMP00003", "SQLSERVER", 4, null, 8, false);
        Skill("EMP00004", "DOTNET", 3, 4, 3, true);
        Skill("EMP00004", "AZURE", 2, 3, 1, false);
        Skill("EMP00005", "ANGULAR", 3, 4, 2, true);
        Skill("EMP00005", "TYPESCRIPT", 3, null, 2, false);
        Skill("EMP00006", "TEST_AUTOMATION", 4, null, 5, true);
        Skill("EMP00006", "PLAYWRIGHT", 3, 4, 2, false);
        Skill("EMP00007", "STAKEHOLDER", 4, null, 7, true);
        db.Add(EmployeeCertification.Record(people["EMP00003"], "Azure Developer Associate", "Microsoft", "AZ-204-1234", today.AddYears(-1), today.AddYears(1), skills["AZURE"]));

        void Profile(string employee, decimal cost, decimal bill, bool billable)
        {
            var profile = ResourceProfile.Create(people[employee]);
            profile.Update(cost, bill, "INR", 40, 60, billable, null);
            db.Add(profile);
        }

        Profile("EMP00001", 4000, 8000, false);
        Profile("EMP00002", 2200, 4500, true);
        Profile("EMP00003", 1800, 3800, true);
        Profile("EMP00004", 1200, 2800, true);
        Profile("EMP00005", 1100, 2600, true);
        Profile("EMP00006", 1000, 2400, true);
        Profile("EMP00007", 1500, 0, false);

        db.Add(ResourceRequest.Raise(project.Id, people["EMP00002"], new RequestDetails(
            null, "Senior Angular developer for checkout", [new SkillRequirement(skills["ANGULAR"], 4), new SkillRequirement(skills["TYPESCRIPT"], 3)],
            4, 1, 100, today.AddDays(14), today.AddMonths(3), true, null, "HYBRID", 2500, RequestPriority.High, "Second front-end pair for checkout.")));

        // Approved timesheets for the last two full weeks, against NWCOM's tasks where people have them.
        List<ProjectTask> tasks = await db.Set<ProjectTask>().Where(task => task.ProjectId == project.Id && task.AssigneeEmployeeId != null && task.Type != TaskType.Epic).ToListAsync();
        DateOnly thisWeek = Timesheet.WeekOf(today);
        foreach (string code in new[] { "EMP00003", "EMP00004", "EMP00005", "EMP00006" })
        {
            Guid employeeId = people[code];
            ProjectTask? task = tasks.FirstOrDefault(row => row.AssigneeEmployeeId == employeeId);
            foreach (DateOnly week in new[] { thisWeek.AddDays(-14), thisWeek.AddDays(-7) })
            {
                Timesheet sheet = Timesheet.Open(employeeId, week);
                List<TimesheetEntry> entries = [];
                for (int day = 0; day < 5; day++)
                {
                    bool meetingHeavy = code == "EMP00006" && day == 4;
                    entries.Add(TimesheetEntry.Book(sheet, week.AddDays(day), project.Id, task?.Id, HourCategory.Billable, meetingHeavy ? 5 : 7, "Development", null));
                    entries.Add(TimesheetEntry.Book(sheet, week.AddDays(day), null, null, HourCategory.Internal, meetingHeavy ? 3 : 1, "Team meetings", null));
                }

                sheet.Recalculate(entries, null);
                sheet.Submit(DateTime.UtcNow, isLate: false);
                sheet.Approve(DateTime.UtcNow);
                db.Add(sheet);
                db.AddRange(entries);
                task?.AddActualHours(entries.Where(entry => entry.TaskId is not null).Sum(entry => entry.Hours));
            }
        }

        await db.SaveChangesAsync();
        Console.WriteLine($"Skills: {catalog} added. Demo resourcing: 14 employee skills, 7 resource profiles, 1 open request, 8 approved timesheets.");
    }
}
