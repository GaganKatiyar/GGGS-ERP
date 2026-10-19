using Gggs.Data;
using Gggs.Modules.Career.Domain;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Identity.Application.Provisioning;
using Gggs.Modules.Identity.Application.Users;
using Gggs.Modules.Identity.Domain.Roles;
using Gggs.Modules.Identity.Domain.Users;
using Gggs.Modules.Learning.Application;
using Gggs.Modules.Learning.Domain;
using Gggs.Modules.Organization.Domain;
using Gggs.Modules.Performance.Application;
using Gggs.Modules.Performance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gggs.DevSeed;

/// <summary>
/// Development-only talent data (M9): company, department and individual goals with check-ins; last
/// half-year's review cycle released with ratings, and this half-year's open with some self-reviews in
/// and 360 feedback asked of the administrator; learning assigned and partly completed; a career
/// ladder, a promotion nomination, a succession plan and potential assessments for the 9-box.
/// </summary>
internal static class TalentDemo
{
    private static readonly string[] Roles = ["PERFORMANCE_ADMIN", "LEARNING_ADMIN", "HRBP"];
    private static readonly string[] LadderJobs = ["DEV", "SR-DEV", "ENG-MGR", "CTO"];

    public static async Task SeedAsync(IServiceProvider scope)
    {
        await TopUpRolesAsync(scope);
        int courses = await scope.GetRequiredService<LearningSeeder>().SeedCurrentTenantAsync();
        Console.WriteLine($"Learning: {courses} default course(s) added.");

        AppDbContext db = scope.GetRequiredService<AppDbContext>();
        IReadOnlyDictionary<string, Guid> people = await scope.GetRequiredService<IEmployeeDirectory>()
            .ResolveCodesAsync(["EMP00001", "EMP00002", "EMP00003", "EMP00004", "EMP00005", "EMP00006", "EMP00007"]);
        if (people.Count < 7)
        {
            Console.WriteLine("Demo employees missing; skipping the talent demo.");
            return;
        }

        await SeedGoalsAsync(db, people);
        await SeedReviewsAsync(scope, db, people);
        await SeedLearningAsync(scope, db, people);
        await SeedCareerAsync(db, people);
    }

    private static async Task SeedGoalsAsync(AppDbContext db, IReadOnlyDictionary<string, Guid> people)
    {
        if (await db.Set<Goal>().AnyAsync())
        {
            Console.WriteLine("Goals already present.");
            return;
        }

        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        var start = new DateOnly(today.Year, today.Month <= 6 ? 1 : 7, 1);
        DateOnly end = start.AddMonths(6).AddDays(-1);
        Guid? platform = (await db.Set<Department>().AsNoTracking().FirstOrDefaultAsync(row => row.Code == "ENG-PLAT"))?.Id
            ?? (await db.Set<Department>().AsNoTracking().FirstAsync()).Id;

        var company = Goal.Create(new GoalDetails("Grow delivery revenue 25%", "Through retained clients and two new logos.", GoalType.Okr, GoalLevel.Company, null, null, null, null, start, end, 0), true);
        var department = Goal.Create(new GoalDetails("Ship the Northwind platform on time", null, GoalType.Okr, GoalLevel.Department, null, platform, company.Id, null, start, end, 0), true);
        db.AddRange(company, department);

        async Task Individual(string code, string title, decimal weight, decimal progress, Confidence confidence, params (string Title, decimal Target, decimal Current)[] results)
        {
            var goal = Goal.Create(new GoalDetails(title, null, GoalType.Okr, GoalLevel.Individual, people[code], platform, department.Id, null, start, end, weight), true);
            db.Add(goal);
            var keyResults = new List<KeyResult>();
            int order = 0;
            foreach ((string resultTitle, decimal target, decimal current) in results)
            {
                var result = KeyResult.Add(goal.Id, new KeyResultDetails(resultTitle, MetricType.Number, 0, target, null, 1), order++);
                result.Record(current);
                keyResults.Add(result);
                db.Add(result);
            }

            decimal done = keyResults.Count > 0 ? KeyResult.Rollup(keyResults) : progress;
            goal.Record(done, confidence, today.AddDays(-3));
            db.Add(GoalCheckIn.Record(goal.Id, people[code], today.AddDays(-3), done, confidence, "Weekly check-in.", [.. keyResults.Select(row => new KeyResultValue(row.Id, row.CurrentValue))]));
            await Task.CompletedTask;
        }

        await Individual("EMP00002", "Deliver NWCOM milestones 3 and 4", 60, 0, Confidence.OnTrack, ("Milestones signed off", 2, 1), ("Sprint goals met", 12, 8));
        await Individual("EMP00003", "Lead the checkout service rewrite", 70, 0, Confidence.OnTrack, ("Endpoints migrated", 20, 14), ("p95 latency under 200 ms", 1, 0));
        await Individual("EMP00004", "Own the payments integration", 60, 0, Confidence.AtRisk, ("Providers integrated", 3, 1));
        await Individual("EMP00005", "Raise test coverage of the cart", 50, 0, Confidence.OnTrack, ("Coverage %", 80, 64));
        await Individual("EMP00006", "Automate the regression suite", 60, 0, Confidence.OffTrack, ("Scenarios automated", 150, 40));
        await Individual("EMP00007", "Launch the onboarding programme", 50, 45, Confidence.OnTrack);
        await db.SaveChangesAsync();
        Console.WriteLine("Goals: a company goal, a department goal and six aligned individual goals with check-ins.");
    }

    private static async Task SeedReviewsAsync(IServiceProvider scope, AppDbContext db, IReadOnlyDictionary<string, Guid> people)
    {
        if (await db.Set<ReviewCycle>().AnyAsync())
        {
            Console.WriteLine("Review cycles already present.");
            return;
        }

        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        DateTime now = DateTime.UtcNow;
        var currentStart = new DateOnly(today.Year, today.Month <= 6 ? 1 : 7, 1);
        DateOnly lastStart = currentStart.AddMonths(-6);
        string[] competencies = ["Delivery", "Collaboration", "Ownership", "Communication"];
        string Half(DateOnly start) => $"{(start.Month == 1 ? "H1" : "H2")} {start.Year}";

        // Last half-year: reviewed, calibrated and released.
        var last = ReviewCycle.Draft(new CycleDetails($"{Half(lastStart)} review", CycleType.HalfYearly, lastStart, currentStart.AddDays(-1), currentStart.AddDays(10), currentStart.AddDays(20), competencies, 50, true, null));
        last.Launch(currentStart);
        db.Add(last);
        Dictionary<string, (int Self, int Manager, int? Calibrated)> ratings = new()
        {
            ["EMP00002"] = (4, 4, null),
            ["EMP00003"] = (5, 5, null),
            ["EMP00004"] = (4, 3, null),
            ["EMP00005"] = (3, 4, 3),
            ["EMP00006"] = (3, 2, null),
            ["EMP00007"] = (4, 4, null),
        };
        IReadOnlyList<EmployeeSummary> summaries = await scope.GetRequiredService<IEmployeeDirectory>().GetManyAsync(people.Values);
        foreach ((string code, (int self, int manager, int? calibrated)) in ratings)
        {
            EmployeeSummary person = summaries.First(row => row.Id == people[code]);
            var review = Review.Open(last.Id, person.Id, person.ManagerEmployeeId);
            Dictionary<string, int> scores = competencies.ToDictionary(name => name, _ => self);
            review.SaveSelf(new ReviewInput(scores, "Delivered what I committed to.", "Delegate more.", null, self), submit: true, now.AddDays(-150));
            review.SaveManager(new ReviewInput(competencies.ToDictionary(name => name, _ => manager), "Reliable and thorough.", "Speak up earlier on risks.", null, manager), submit: true, 70, manager, now.AddDays(-145));
            if (calibrated is { } value)
            {
                review.Calibrate(value, "Aligned with the team's distribution.");
            }

            review.Release(now.AddDays(-140));
            db.Add(review);
        }

        last.MarkReleased(currentStart.AddDays(25));
        last.Close();
        await db.SaveChangesAsync();

        // This half-year: open, with a couple of self-reviews in and feedback asked of the administrator.
        ReviewService reviews = scope.GetRequiredService<ReviewService>();
        CycleItem current = await reviews.SaveCycleAsync(null, new CycleDetails(
            $"{Half(currentStart)} review", CycleType.HalfYearly, currentStart, currentStart.AddMonths(6).AddDays(-1), today.AddDays(14), today.AddDays(28), competencies, 50, true, null), CancellationToken.None);
        await reviews.LaunchAsync(current.Id, CancellationToken.None);
        List<Review> open = await db.Set<Review>().Where(row => row.CycleId == current.Id).ToListAsync();
        foreach (string code in new[] { "EMP00003", "EMP00005" })
        {
            Review review = open.First(row => row.EmployeeId == people[code]);
            review.SaveSelf(new ReviewInput(competencies.ToDictionary(name => name, _ => 4), "Took on the hardest module.", "Documentation.", null, 4), submit: true, now);
        }

        Review meera = open.First(row => row.EmployeeId == people["EMP00003"]);
        db.Add(FeedbackRequest.Ask(meera.Id, people["EMP00003"], people["EMP00007"], people["EMP00003"], FeedbackRelationship.Peer));
        db.Add(FeedbackRequest.Ask(meera.Id, people["EMP00003"], people["EMP00004"], people["EMP00003"], FeedbackRelationship.Peer));
        await db.SaveChangesAsync();
        Console.WriteLine($"Reviews: {Half(lastStart)} released for {ratings.Count} people; {Half(currentStart)} open for {open.Count}.");
    }

    private static async Task SeedLearningAsync(IServiceProvider scope, AppDbContext db, IReadOnlyDictionary<string, Guid> people)
    {
        if (await db.Set<Enrollment>().AnyAsync())
        {
            Console.WriteLine("Learning enrollments already present.");
            return;
        }

        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        LearningService learning = scope.GetRequiredService<LearningService>();
        foreach (Guid person in people.Values)
        {
            await learning.AssignJoinerAsync(person, today.AddDays(-20), CancellationToken.None);
        }

        Dictionary<Guid, Course> courses = await db.Set<Course>().AsNoTracking().ToDictionaryAsync(row => row.Id);
        List<Enrollment> enrollments = await db.Set<Enrollment>().ToListAsync();
        int index = 0;
        foreach (Enrollment enrollment in enrollments)
        {
            Course course = courses[enrollment.CourseId];
            switch (index++ % 3)
            {
                case 0:
                    enrollment.Complete(today.AddDays(-5), 90, course.CertificateValidityMonths, $"{course.Code}-{today.Year}-{enrollment.Id.ToString("N")[..8].ToUpperInvariant()}");
                    enrollment.Rate(4, null);
                    break;
                case 1:
                    enrollment.UpdateProgress(50, today.AddDays(-2));
                    break;
            }
        }

        Guid infosec = courses.Values.First(row => row.Code == "INFOSEC").Id;
        Guid privacy = courses.Values.First(row => row.Code == "DATAPRIV").Id;
        db.Add(LearningPath.Create("SECURE-ENG", "Secure engineering", "Security and privacy basics for every engineer.", [infosec, privacy]));
        await db.SaveChangesAsync();
        Console.WriteLine($"Learning: {enrollments.Count} enrollment(s) for joiners' mandatory courses, a third completed; a security path.");
    }

    private static async Task SeedCareerAsync(AppDbContext db, IReadOnlyDictionary<string, Guid> people)
    {
        if (await db.Set<CareerPath>().AnyAsync())
        {
            Console.WriteLine("Career demo already present.");
            return;
        }

        Dictionary<string, Guid> jobs = await db.Set<Job>().AsNoTracking().ToDictionaryAsync(row => row.Code, row => row.Id);
        if (LadderJobs.Any(code => !jobs.TryGetValue(code, out _)))
        {
            Console.WriteLine("Demo jobs missing; skipping career paths.");
            return;
        }

        db.Add(CareerPath.Create("Software engineering", "From engineer to senior engineer.", CareerTrack.Individual, null,
        [
            new CareerStep("Software engineer", jobs["DEV"], null, 18, [".NET", "Angular"], null),
            new CareerStep("Senior software engineer", jobs["SR-DEV"], null, 24, ["System design", "SQL Server", "Mentoring"], null),
        ]));
        db.Add(CareerPath.Create("Engineering leadership", "Leading teams, then the function.", CareerTrack.Management, null,
        [
            new CareerStep("Senior software engineer", jobs["SR-DEV"], null, 24, ["System design"], null),
            new CareerStep("Engineering manager", jobs["ENG-MGR"], null, 36, ["People management", "Azure"], null),
            new CareerStep("Chief technology officer", jobs["CTO"], null, 0, ["Architecture", "Leadership"], null),
        ]));

        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        db.Add(SuccessionPlan.Create("Platform engineering manager", null, null, people["EMP00002"], Criticality.High, Criticality.Medium, "Owns the Northwind relationship.",
            [new SuccessorEntry(people["EMP00003"], Readiness.ReadyNow, "Already runs sprint planning."), new SuccessorEntry(people["EMP00004"], Readiness.ThreeToFiveYears, null)]));
        db.Add(SuccessionPlan.Create("Chief technology officer", null, null, people["EMP00001"], Criticality.Critical, Criticality.Low, null,
            [new SuccessorEntry(people["EMP00002"], Readiness.OneToTwoYears, "Needs exposure to finance.")]));

        db.AddRange(
            TalentAssessment.Record(people["EMP00002"], Level.High, Level.Low, null, people["EMP00001"], today),
            TalentAssessment.Record(people["EMP00003"], Level.High, Level.Medium, "Ready for a lead role.", people["EMP00002"], today),
            TalentAssessment.Record(people["EMP00004"], Level.Medium, Level.Low, null, people["EMP00002"], today),
            TalentAssessment.Record(people["EMP00005"], Level.Medium, Level.Low, null, people["EMP00002"], today),
            TalentAssessment.Record(people["EMP00006"], Level.Low, Level.High, "Has an offer elsewhere.", people["EMP00002"], today));
        await db.SaveChangesAsync();

        // A nomination waiting for submission: Arjun to senior engineer.
        db.Add(PromotionNomination.Draft(people["EMP00004"], people["EMP00002"], "Engineer, platform", 1_100_000, 3,
            new PromotionDetails("Senior software engineer", jobs["SR-DEV"], null, null, 1_450_000, new DateOnly(today.Year, today.Month, 1).AddMonths(1), "Owns the payments integration end to end.")));
        await db.SaveChangesAsync();
        Console.WriteLine("Career: two paths, two succession plans, five potential assessments and a promotion nomination.");
    }

    /// <summary>Tenant roles are copies made at provisioning; topped up from the templates for M9 - development only.</summary>
    private static async Task TopUpRolesAsync(IServiceProvider scope)
    {
        AppDbContext db = scope.GetRequiredService<AppDbContext>();
        IReadOnlyDictionary<string, Role> roles = await scope.GetRequiredService<IRoleRepository>().GetTemplateRolesAsync();
        int added = 0;
        foreach (string code in Roles)
        {
            if (roles.TryGetValue(code, out Role? role))
            {
                int before = role.Permissions.Count;
                foreach (PermissionAssignment permission in RoleTemplates.For(code))
                {
                    role.Grant(permission.PermissionCode, permission.Scope);
                }

                added += role.Permissions.Count - before;
            }
        }

        if (added > 0)
        {
            foreach (User user in await db.Set<User>().ToListAsync())
            {
                user.BumpPermissionVersion();
            }

            await db.SaveChangesAsync();
        }

        Console.WriteLine($"Talent roles: {added} permission(s) added from the templates.");
    }
}
