using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Identity.Application.Provisioning;
using Gggs.Modules.Identity.Application.Users;
using Gggs.Modules.Identity.Domain.Roles;
using Gggs.Modules.Identity.Domain.Users;
using Gggs.Modules.Onboarding.Application;
using Gggs.Modules.Onboarding.Domain;
using Gggs.Modules.Recruitment.Application;
using Gggs.Modules.Recruitment.Domain;
using Gggs.Modules.Workforce.Application;
using Gggs.Modules.Workforce.Contracts;
using Gggs.Modules.Workforce.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using EmployeeEntity = Gggs.Modules.Employee.Domain.Employee;

namespace Gggs.DevSeed;

/// <summary>
/// Development-only hiring data (M8): this year's approved workforce plan, manpower requests, two
/// requisitions with a posting, candidates across the pipeline with interviews, a scorecard and an
/// offer out with a candidate, an agency, a referral, and onboarding for the newest demo joiner.
/// </summary>
internal static class HiringDemo
{
    private static readonly string[] Roles = ["TENANT_ADMIN", "HR_ADMIN", "RECRUITER", "HIRING_MANAGER", "DEPARTMENT_MANAGER", "TEAM_LEAD", "IT_ADMIN", "EMPLOYEE"];

    public static async Task SeedAsync(IServiceProvider scope)
    {
        await TopUpRolesAsync(scope);
        int checklists = await scope.GetRequiredService<OnboardingSeeder>().SeedCurrentTenantAsync();
        Console.WriteLine($"Onboarding: {checklists} default checklist(s) added.");

        AppDbContext db = scope.GetRequiredService<AppDbContext>();
        IReadOnlyDictionary<string, Guid> people = await scope.GetRequiredService<IEmployeeDirectory>()
            .ResolveCodesAsync(["EMP00001", "EMP00002", "EMP00003", "EMP00004", "EMP00005", "EMP00006", "EMP00007"]);
        if (people.Count < 7)
        {
            Console.WriteLine("Demo employees missing; skipping the hiring demo.");
            return;
        }

        Guid anita = people["EMP00001"];
        Guid vikram = people["EMP00002"];
        Guid meera = people["EMP00003"];
        Guid sara = people["EMP00005"];
        Guid priya = people["EMP00007"];
        EmployeeEntity lead = await db.Set<EmployeeEntity>().AsNoTracking().FirstAsync(employee => employee.Id == vikram);
        if (lead.DepartmentId is not { } department)
        {
            Console.WriteLine("The demo lead has no department; skipping the hiring demo.");
            return;
        }

        await SeedPlanAsync(scope, db, anita);
        await SeedRecruitmentAsync(scope, db, department, vikram, meera, sara, priya);
        await SeedOnboardingAsync(scope, db, priya, people["EMP00006"]);
    }

    private static async Task SeedPlanAsync(IServiceProvider scope, AppDbContext db, Guid owner)
    {
        if (await db.Set<WorkforcePlan>().AnyAsync())
        {
            Console.WriteLine("Workforce plan already present.");
            return;
        }

        int year = DateTime.UtcNow.Year;
        WorkforceService workforce = scope.GetRequiredService<WorkforceService>();
        PlanView plan = await workforce.SavePlanAsync(null, new PlanBody($"{year} headcount plan", year, new DateOnly(year, 4, 1), owner, "Growth plan for the delivery practice."), CancellationToken.None);

        var counts = await db.Set<EmployeeEntity>().AsNoTracking().Where(employee => employee.ExitDate == null && employee.DepartmentId != null)
            .GroupBy(employee => employee.DepartmentId!.Value)
            .Select(group => new { Department = group.Key, Count = group.Count() })
            .ToListAsync();
        List<PlanLineDetails> lines = [.. counts.Select(row => new PlanLineDetails(row.Department, null, null, row.Count + 2, (row.Count + 2) * 1_800_000m, 2 * 150_000m, null))];
        await workforce.SaveLinesAsync(plan.Plan.Id, new PlanLinesBody(lines), CancellationToken.None);
        await workforce.SubmitPlanAsync(plan.Plan.Id, CancellationToken.None);
        Console.WriteLine($"Workforce plan {year}: {lines.Count} department line(s), approved.");
    }

    private static async Task SeedRecruitmentAsync(IServiceProvider scope, AppDbContext db, Guid department, Guid lead, Guid recruiter, Guid panelist, Guid referrer)
    {
        if (await db.Set<Requisition>().AnyAsync())
        {
            Console.WriteLine("Recruitment demo already present.");
            return;
        }

        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        DateTime now = DateTime.UtcNow;
        RecruitmentService recruitment = scope.GetRequiredService<RecruitmentService>();
        PipelineService pipeline = scope.GetRequiredService<PipelineService>();
        OfferService offers = scope.GetRequiredService<OfferService>();

        // A manager's request, approved into a requisition; another still waiting for approval.
        var approved = ManpowerRequest.Raise($"MPR-{today.Year}-0001", lead, new RequestDetails(
            "Senior .NET Engineer", department, null, null, null, 2, ["C#", "ASP.NET Core", "SQL Server", "Azure"], 4, 9, "FULL_TIME", "HYBRID",
            1_800_000m, 2_800_000m, 5_200_000m, RequestPriority.High, today.AddDays(45), RequestReason.Expansion, null, "Northwind modernisation needs two more seniors."));
        approved.Submit();
        approved.Decide(approved: true, now);
        var pending = ManpowerRequest.Raise($"MPR-{today.Year}-0002", lead, new RequestDetails(
            "QA Automation Engineer", department, null, null, null, 1, ["Playwright", "TypeScript"], 2, 5, "FULL_TIME", "OFFICE",
            900_000m, 1_400_000m, 1_400_000m, RequestPriority.Medium, today.AddDays(60), RequestReason.NewRole, null, "Release testing is the bottleneck."));
        pending.Submit();
        db.AddRange(approved, pending);
        await db.SaveChangesAsync();

        await recruitment.OpenFromRequestAsync(new ManpowerRequestApprovedIntegrationEvent(
            approved.Id, approved.Number, approved.Title, approved.DepartmentId, null, null, null, approved.Positions, approved.EmploymentTypeCode, approved.WorkModeCode,
            approved.MinExperienceYears, approved.MaxExperienceYears, approved.SalaryMin, approved.SalaryMax, approved.Skills, approved.RequiredBy, lead,
            approved.Priority.ToString(), approved.Justification), CancellationToken.None);
        Requisition senior = await db.Set<Requisition>().FirstAsync(row => row.ManpowerRequestId == approved.Id);
        senior.Update(new RequisitionDetails(
            senior.Title, senior.DepartmentId, null, null, null, senior.Openings, senior.EmploymentTypeCode, senior.WorkModeCode, senior.MinExperienceYears, senior.MaxExperienceYears,
            senior.SalaryMin, senior.SalaryMax, senior.Skills,
            "Lead the API modernisation for our largest retail client: design services on ASP.NET Core, own the SQL Server data layer and ship to Azure.",
            lead, recruiter, "High", senior.TargetDate ?? today.AddDays(45)));
        await db.SaveChangesAsync();

        RequisitionItem designer = await recruitment.CreateRequisitionAsync(new RequisitionDetails(
            "Product Designer", department, null, null, null, 1, "FULL_TIME", "REMOTE", 3, 7, 1_200_000m, 1_900_000m, ["Figma", "Design systems", "User research"],
            "Own the end-to-end design of PeopleOS self-service.", lead, recruiter, "Medium", today.AddDays(30)), CancellationToken.None);

        PostingItem posting = await recruitment.SavePostingAsync(null, new PostingBody(senior.Id, senior.Title, senior.Description ?? senior.Title, true, true, today.AddDays(30)), CancellationToken.None);
        await recruitment.ChangePostingAsync(posting.Id, publish: true, CancellationToken.None);
        await recruitment.SaveAgencyAsync(null, new AgencyBody("TalentBridge Search", "Nisha Kapoor", "nisha@talentbridge.example", "+91 80 4000 2000", 8.33m), CancellationToken.None);
        Guid agency = (await db.Set<Agency>().FirstAsync()).Id;

        // Candidates across the pipeline.
        Guid rahul = await AddAsync(pipeline, senior.Id, "Rahul", "Verma", "rahul.verma@mail.example", "+91 98450 11111", "Infosys", "Technology Lead", 7, 2_100_000m, 2_600_000m, ["C#", "ASP.NET Core", "SQL Server", "Azure", "Kafka"], CandidateSource.JobBoard, null, null);
        Guid divya = await AddAsync(pipeline, senior.Id, "Divya", "Nair", "divya.nair@mail.example", "+91 98450 22222", "ThoughtWorks", "Senior Consultant", 6, 2_000_000m, 2_500_000m, ["C#", "ASP.NET Core", "SQL Server"], CandidateSource.Referral, null, referrer);
        Guid karan = await AddAsync(pipeline, senior.Id, "Karan", "Shah", "karan.shah@mail.example", "+91 98450 33333", "Wipro", "Senior Engineer", 5, 1_600_000m, 2_200_000m, ["C#", "SQL Server", "Angular"], CandidateSource.Agency, agency, null);
        await AddAsync(pipeline, senior.Id, "Neha", "Gupta", "neha.gupta@mail.example", null, "Startup", "Engineer", 2, 900_000m, 1_500_000m, ["Node.js", "MongoDB"], CandidateSource.Careers, null, null);
        Guid ishaan = await AddAsync(pipeline, designer.Id, "Ishaan", "Rao", "ishaan.rao@mail.example", "+91 98450 55555", "Swiggy", "Product Designer", 4, 1_400_000m, 1_800_000m, ["Figma", "Design systems", "Prototyping"], CandidateSource.Social, null, null);
        await AddAsync(pipeline, designer.Id, "Tara", "Menon", "tara.menon@mail.example", null, "Freelance", "UX Designer", 3, null, 1_300_000m, ["Figma", "User research"], CandidateSource.Direct, null, null);

        Guid Application(Guid candidate) => db.Set<JobApplication>().AsNoTracking().First(row => row.CandidateId == candidate).Id;

        // Rahul: interviewed, strong feedback, offer approved and sent.
        Guid rahulApplication = Application(rahul);
        await pipeline.MoveAsync(rahulApplication, new MoveBody(ApplicationStage.Shortlisted, "Strong platform background"), CancellationToken.None);
        ApplicationView interviewed = await pipeline.ScheduleAsync(rahulApplication, new InterviewBody("Technical 1", now.AddDays(-3), 60, InterviewMode.Video, "https://meet.example/tech-1", [panelist], null), CancellationToken.None);
        Guid interview = interviewed.Interviews[0].Id;
        db.Add(Scorecard.Submit(interview, rahulApplication, panelist, new Dictionary<string, int> { ["Technical skills"] = 5, ["Problem solving"] = 4, ["Communication"] = 4, ["Culture fit"] = 5 }, Recommendation.StrongYes, "Excellent system design; led a similar migration.", now.AddDays(-3)));
        (await db.Set<Interview>().FirstAsync(row => row.Id == interview)).Complete();
        await db.SaveChangesAsync();
        OfferItem offer = await offers.DraftAsync(new OfferBody(rahulApplication, new OfferTerms(2_500_000m, "INR", today.AddDays(40), "Senior Software Engineer", null, today.AddDays(7), "Joining bonus discussed separately."), Submit: true), CancellationToken.None);
        await offers.ActAsync(offer.Id, "send", null, CancellationToken.None);

        // Divya (referred): interview coming up. Karan (agency): screening. Ishaan: shortlisted for design.
        Guid divyaApplication = Application(divya);
        await pipeline.MoveAsync(divyaApplication, new MoveBody(ApplicationStage.Screening, null), CancellationToken.None);
        await pipeline.ScheduleAsync(divyaApplication, new InterviewBody("Technical 1", now.AddDays(2).Date.AddHours(5), 60, InterviewMode.Video, "https://meet.example/tech-2", [panelist, recruiter], null), CancellationToken.None);
        await pipeline.MoveAsync(Application(karan), new MoveBody(ApplicationStage.Screening, "Agency profile, notice 60 days"), CancellationToken.None);
        await pipeline.MoveAsync(Application(ishaan), new MoveBody(ApplicationStage.Shortlisted, "Great portfolio"), CancellationToken.None);
        await pipeline.InitiateCheckAsync(new BackgroundCheckBody(rahul, rahulApplication, "VerifyCo", ["Identity", "Education", "Employment"]), CancellationToken.None);

        Console.WriteLine("Recruitment: 2 manpower requests, 2 requisitions, a posting, an agency, 6 candidates, 2 interviews and an offer out.");
    }

    private static async Task SeedOnboardingAsync(IServiceProvider scope, AppDbContext db, Guid hr, Guid joiner)
    {
        OnboardingService onboarding = scope.GetRequiredService<OnboardingService>();
        if (await db.Set<OnboardingSettings>().AnyAsync() || await db.Set<OnboardingCase>().AnyAsync())
        {
            Console.WriteLine("Onboarding demo already present.");
            return;
        }

        await onboarding.SaveOwnersAsync(new OwnersBody(hr, hr, hr), CancellationToken.None);
        CaseView started = await onboarding.StartAsync(new StartBody(joiner, null, null), CancellationToken.None);
        Console.WriteLine($"Onboarding: started for {started.Case.EmployeeName} with {started.Tasks.Count} task(s).");
    }

    private static async Task<Guid> AddAsync(
        PipelineService pipeline,
        Guid requisition,
        string first,
        string last,
        string email,
        string? mobile,
        string company,
        string title,
        decimal experience,
        decimal? current,
        decimal expected,
        string[] skills,
        CandidateSource source,
        Guid? agency,
        Guid? referrer)
    {
        CandidateView view = await pipeline.AddCandidateAsync(
            new CandidateBody(
                new CandidateDetails(first, last, email, mobile, company, title, experience, current, expected, 30, "Bengaluru", skills, null, null),
                source,
                agency,
                referrer,
                requisition),
            CancellationToken.None);
        return view.Candidate.Id;
    }

    /// <summary>
    /// Tenant roles are copies made at provisioning, so a tenant created before M8 lacks the hiring
    /// grants. Topped up from the templates - development only.
    /// </summary>
    private static async Task TopUpRolesAsync(IServiceProvider scope)
    {
        AppDbContext db = scope.GetRequiredService<AppDbContext>();
        IReadOnlyDictionary<string, Role> roles = await scope.GetRequiredService<IRoleRepository>().GetTemplateRolesAsync();
        int added = 0;
        foreach (string code in Roles)
        {
            if (!roles.TryGetValue(code, out Role? role))
            {
                continue;
            }

            int before = role.Permissions.Count;
            foreach (PermissionAssignment permission in RoleTemplates.For(code))
            {
                role.Grant(permission.PermissionCode, permission.Scope);
            }

            added += role.Permissions.Count - before;
        }

        if (added > 0)
        {
            foreach (User user in await db.Set<User>().ToListAsync())
            {
                user.BumpPermissionVersion();
            }

            await db.SaveChangesAsync();
        }

        Console.WriteLine($"Hiring roles: {added} permission(s) added from the templates.");
    }
}
