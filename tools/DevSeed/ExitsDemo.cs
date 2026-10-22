using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.Data;
using Gggs.Modules.Attendance.Domain;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Payroll.Application;
using Gggs.Modules.Payroll.Contracts;
using Gggs.Modules.Payroll.Domain;
using Gggs.Modules.Recruitment.Domain;
using Gggs.Modules.Separation.Application;
using Gggs.Modules.Separation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gggs.DevSeed;

/// <summary>
/// Separation v2 demo: five people who left over the last few months, each through the real flows -
/// a regretted resignation paid through payroll, a resignation over pay settled by bank reference, a
/// termination after a show-cause, a retrenchment with compensation, and an early leaver - with
/// exit reasons, survey answers, settlements and the exit itself, so attrition analytics, alumni and
/// the full and final payroll runs have something to show. A former employee applies again, and an
/// absence long enough to look like absconding is waiting for HR.
/// </summary>
internal static class ExitsDemo
{
    private sealed record Leaver(
        string First,
        string Last,
        string Designation,
        DateOnly Joined,
        decimal? AnnualCtc,
        SeparationType Type,
        string ReasonCode,
        string Reason,
        bool? Regretted,
        string? RetentionNote,
        DateOnly NoticeDate,
        DateOnly LastDay,
        int PayInLieu,
        IReadOnlyDictionary<string, string>? Survey,
        bool PayThroughPayroll);

    public static async Task SeedAsync(IServiceProvider scope)
    {
        AppDbContext db = scope.GetRequiredService<AppDbContext>();
        if (await db.Set<AlumniProfile>().AnyAsync(row => row.Name == "Neha Joshi"))
        {
            Console.WriteLine("Exit demo already present.");
            return;
        }

        IEmployeeDirectory directory = scope.GetRequiredService<IEmployeeDirectory>();
        IReadOnlyDictionary<string, Guid> codes = await directory.ResolveCodesAsync(["EMP00002", "EMP00004", "EMP00005"]);
        if (codes.Count < 3)
        {
            Console.WriteLine("Demo employees missing; skipping the exit demo.");
            return;
        }

        EmployeeSummary template = (await directory.GetAsync(codes["EMP00005"]))!;
        Guid manager = codes["EMP00002"];
        Guid structure = await db.Set<SalaryStructure>().Where(row => row.Code == PayrollDefaults.StandardStructureCode).Select(row => row.Id).FirstAsync();

        Leaver[] leavers =
        [
            new("Neha", "Joshi", "Senior engineer", new DateOnly(2020, 6, 15), 1_440_000m, SeparationType.Resignation, "CAREER_GROWTH",
                "Offered a staff engineer role at a product company.", true, "Counter-offer of 12% and a promotion path discussed; declined.",
                new DateOnly(2026, 7, 1), new DateOnly(2026, 8, 29), 0,
                new Dictionary<string, string> { ["overall"] = "4", ["manager"] = "4", ["growth"] = "2", ["pay"] = "3", ["main-reason"] = "Career growth", ["recommend"] = "yes", ["return"] = "yes", ["improve"] = "Clearer growth paths for senior engineers." },
                PayThroughPayroll: true),
            new("Karan", "Mehta", "Business analyst", new DateOnly(2023, 3, 1), null, SeparationType.Resignation, "COMPENSATION",
                "Joining a consultancy at a much higher salary.", false, null,
                new DateOnly(2026, 7, 20), new DateOnly(2026, 9, 18), 0,
                new Dictionary<string, string> { ["overall"] = "3", ["manager"] = "3", ["growth"] = "3", ["pay"] = "1", ["main-reason"] = "Compensation", ["recommend"] = "no", ["return"] = "no", ["improve"] = "Pay at market rates." },
                PayThroughPayroll: false),
            new("Divya", "Rao", "Support engineer", new DateOnly(2024, 5, 6), 720_000m, SeparationType.Termination, "PERFORMANCE",
                "Terminated after the show-cause on repeated SLA breaches.", null, null,
                new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 25), 15, null, PayThroughPayroll: true),
            new("Imran", "Shaikh", "Operations analyst", new DateOnly(2021, 11, 8), 960_000m, SeparationType.Retrenchment, "RESTRUCTURING",
                "Closure of the Pune operations desk.", null, null,
                new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), 0, null, PayThroughPayroll: true),
            new("Pooja", "Nair", "Associate engineer", new DateOnly(2026, 5, 4), 540_000m, SeparationType.Resignation, "HIGHER_STUDIES",
                "Admitted to a master's programme abroad.", true, "Offered a study sabbatical; she preferred to resign.",
                new DateOnly(2026, 8, 17), new DateOnly(2026, 10, 9), 0,
                new Dictionary<string, string> { ["overall"] = "5", ["manager"] = "5", ["growth"] = "4", ["pay"] = "4", ["main-reason"] = "Higher studies", ["recommend"] = "yes", ["return"] = "yes" },
                PayThroughPayroll: true),
        ];

        IEmployeeHiring hiring = scope.GetRequiredService<IEmployeeHiring>();
        IPayrollSalaries salaries = scope.GetRequiredService<IPayrollSalaries>();
        SeparationService separations = scope.GetRequiredService<SeparationService>();
        OffboardingService offboarding = scope.GetRequiredService<OffboardingService>();
        await scope.GetRequiredService<ExitSurveyService>().GetAsync(CancellationToken.None);
        ExitSurvey survey = await db.Set<ExitSurvey>().FirstAsync();

        var cases = new Dictionary<string, Guid>();
        int mobile = 61000;
        foreach (Leaver leaver in leavers)
        {
            mobile += 111;
            string email = $"{leaver.First.ToLowerInvariant()}.{leaver.Last.ToLowerInvariant()}@gmail.example";
            (Guid person, _) = await hiring.HireAsync(new NewHire(
                leaver.First, null, leaver.Last, email, $"98450{mobile}", leaver.Joined,
                template.DepartmentId, null, null, null, template.LocationId, manager, "FULL_TIME", "HYBRID", leaver.Designation));
            if (leaver.AnnualCtc is { } ctc)
            {
                await salaries.ReviseAsync(new SalaryRevisionRequest(person, ctc, leaver.Joined, structure, "Hire", "dev-seed", null));
            }

            CaseView view = await separations.InitiateAsync(
                new InitiateBody(person, leaver.Type, leaver.Reason, leaver.NoticeDate, leaver.LastDay, null, leaver.ReasonCode, leaver.PayInLieu), CancellationToken.None);
            Guid caseId = view.Case.Id;
            cases[leaver.First] = caseId;
            if (leaver.Regretted is not null)
            {
                await separations.ClassifyAsync(caseId, new ClassificationBody(leaver.ReasonCode, null, leaver.Regretted, leaver.RetentionNote), CancellationToken.None);
            }

            foreach (ClearanceView item in view.Clearance)
            {
                await separations.ResolveAsync(item.Id, new ResolveBody(ClearanceStatus.Cleared, null, "Done before the last day"), CancellationToken.None);
            }

            foreach (OffboardingTaskView task in view.Offboarding?.Tasks ?? [])
            {
                await offboarding.FinishTaskAsync(task.Id, new FinishTaskBody(false, null), CancellationToken.None);
            }

            if (leaver.Survey is { } answers)
            {
                db.Add(ExitSurveyResponse.Submit(caseId, survey.Validate(answers), hiddenFromManager: leaver.Type == SeparationType.Resignation && leaver.Regretted == false, leaver.LastDay.AddDays(-3)));
                await db.SaveChangesAsync();
            }

            await separations.CalculateAsync(caseId, CancellationToken.None);
            await separations.SettlementActionAsync(caseId, "approve", null, CancellationToken.None);
            if (!leaver.PayThroughPayroll)
            {
                await separations.SettlementActionAsync(caseId, "pay", "NEFT-88213", CancellationToken.None);
            }
        }

        // Divya's termination followed a show-cause she answered.
        await DivyaNoticeAsync(db, separations, cases["Divya"], manager);

        // Pay two settlements through their payroll runs; leave the others for payroll to process.
        RunService runs = scope.GetRequiredService<RunService>();
        IFinalSettlementPayroll payroll = scope.GetRequiredService<IFinalSettlementPayroll>();
        foreach (string first in new[] { "Neha", "Imran" })
        {
            Guid settlementId = (await separations.CaseAsync(cases[first], CancellationToken.None)).Settlement!.Id;
            FinalSettlementRun? run = await payroll.FindAsync(settlementId);
            if (run is null)
            {
                continue;
            }

            SubmitRunOutcome submitted = await runs.SubmitAsync(run.RunId, CancellationToken.None);
            if (submitted.Status != RunStatus.Approved)
            {
                Console.WriteLine($"Payroll approval is needed for {first}'s full and final run; left for payroll.");
                continue;
            }

            await runs.FinalizeAsync(run.RunId, CancellationToken.None);
            DateOnly paidOn = leavers.Single(leaver => leaver.First == first).LastDay.AddDays(2);
            await runs.MarkPaidAsync(run.RunId, new PaidBody(paidOn, $"NEFT-FNF-{first.ToUpperInvariant()}"), CancellationToken.None);
            await separations.SettlementPaidByPayrollAsync(settlementId, paidOn, $"NEFT-FNF-{first.ToUpperInvariant()}", CancellationToken.None);
        }

        // The exits take effect: Core HR records them and everyone joins the alumni network.
        int exited = await separations.SweepAsync(CancellationToken.None);

        // Neha applies again from her personal email: Recruitment flags her as a former employee.
        db.Add(Candidate.Create("CAN-900001", new CandidateDetails(
            "Neha", "Joshi", "neha.joshi@gmail.example", null, "Product company", "Staff engineer", 8m, 2_600_000m, 2_800_000m, 60, "Bengaluru", ["C#", "Distributed systems"], null,
            "Interested in coming back as a principal engineer."), CandidateSource.Direct, null, null));

        // Arjun stopped coming in after a day at work 16 days ago: a real absconding alert.
        Guid arjun = codes["EMP00004"];
        ITenantClock clock = scope.GetRequiredService<ITenantClock>();
        DateOnly worked = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-16);
        while (worked.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            worked = worked.AddDays(-1);
        }

        db.Add(AttendancePunch.Record(arjun, await clock.ToUtcAsync(worked, new TimeOnly(9, 10)), PunchSource.Device));
        db.Add(AttendancePunch.Record(arjun, await clock.ToUtcAsync(worked, new TimeOnly(18, 5)), PunchSource.Device));
        await db.SaveChangesAsync();
        int raised = await scope.GetRequiredService<InvoluntaryService>().DetectAbsencesAsync(CancellationToken.None);

        Console.WriteLine($"Exit demo: 5 leavers ({exited} exit(s) taken into effect), 2 settlements paid through payroll and 1 by reference, a returning candidate and {raised} absence alert(s).");
    }

    /// <summary>The show-cause behind Divya's termination: issued, answered, then decided.</summary>
    private static async Task DivyaNoticeAsync(AppDbContext db, SeparationService separations, Guid caseId, Guid manager)
    {
        Guid employee = (await separations.CaseAsync(caseId, CancellationToken.None)).Case.EmployeeId;
        var notice = ShowCause.Issue(employee, "Breached response SLAs on 14 priority tickets in August despite coaching.", new DateOnly(2026, 8, 25), 7, manager);
        notice.RecordReply("The queue doubled after two colleagues left; I asked for help twice.", new DateOnly(2026, 8, 29));
        notice.Decide(ShowCauseOutcome.Termination, "Reply considered; the breaches continued after the August review.", new DateOnly(2026, 9, 10));
        notice.LedTo(caseId);
        db.Add(notice);
        await db.SaveChangesAsync();
    }
}
