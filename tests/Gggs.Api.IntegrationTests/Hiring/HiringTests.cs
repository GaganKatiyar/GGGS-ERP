using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Gggs.Modules.Onboarding.Application;
using Gggs.Modules.Recruitment.Application;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using static Gggs.Api.IntegrationTests.DeliveryApi;
using static Gggs.Api.IntegrationTests.PlatformServicesApi;

namespace Gggs.Api.IntegrationTests;

/// <summary>
/// M8 end to end: a workforce plan, a manpower request checked against it and approved into a
/// requisition, the recruitment pipeline from posting and referral through interviews, scorecards and
/// an offer to a hire, and the onboarding that follows.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class HiringTests(PeopleOsApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static object Candidate(string first, string? email = null, string? mobile = null, string[]? skills = null, decimal? experience = 5, decimal? expected = 1_400_000) => new
    {
        FirstName = first,
        LastName = CoreHrApi.Unique("Cand"),
        Email = email ?? Email(first.ToLowerInvariant()),
        Mobile = mobile,
        CurrentCompany = "Elsewhere Ltd",
        CurrentTitle = "Engineer",
        ExperienceYears = experience,
        CurrentCtc = 1_100_000m,
        ExpectedCtc = expected,
        NoticePeriodDays = 30,
        Location = "Bengaluru",
        Skills = skills ?? new[] { "C#", "SQL" },
    };

    private static object Requisition(Guid department, int openings = 1, Guid? hiringManager = null) => new
    {
        Title = "Backend Engineer " + CoreHrApi.Unique("R"),
        DepartmentId = department,
        Openings = openings,
        EmploymentTypeCode = "FULL_TIME",
        WorkModeCode = "HYBRID",
        MinExperienceYears = 3m,
        MaxExperienceYears = 8m,
        SalaryMin = 1_000_000m,
        SalaryMax = 1_500_000m,
        Skills = new[] { "C#", "SQL", "Azure" },
        Description = "Build and run our APIs.",
        HiringManagerEmployeeId = hiringManager,
    };

    [Fact]
    public async Task A_plan_and_an_approved_manpower_request_open_a_requisition_that_a_hire_fulfils()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid department = await CoreHrApi.DepartmentAsync(admin);
        Guid manager = await CoreHrApi.HireAsync(admin, department);
        int year = Today.Year;

        // The plan: three people in the department, one there today.
        JsonElement plan = await OkAsync(await admin.PostAsJsonAsync("/api/v1/workforce/plans", new { Name = "Plan " + CoreHrApi.Unique("P"), Year = year, EffectiveDate = Iso(new DateOnly(year, 1, 1)) }, Ct));
        Guid planId = plan.GetProperty("plan").GetProperty("id").GetGuid();
        JsonElement lined = await OkAsync(await admin.PutAsJsonAsync($"/api/v1/workforce/plans/{planId}/lines", new
        {
            Lines = new[] { new { DepartmentId = department, PlannedHeadcount = 3, SalaryBudget = 4_500_000m, HiringBudget = 300_000m } },
        }, Ct));
        JsonElement line = lined.GetProperty("lines")[0];
        line.GetProperty("currentHeadcount").GetInt32().ShouldBe(1);
        line.GetProperty("headcountGap").GetInt32().ShouldBe(2);

        JsonElement submitted = await OkAsync(await admin.PostAsync($"/api/v1/workforce/plans/{planId}/submit", null, Ct));
        submitted.GetProperty("plan").GetProperty("status").GetString().ShouldBe("Approved");
        (await admin.PutAsJsonAsync($"/api/v1/workforce/plans/{planId}/lines", new { Lines = Array.Empty<object>() }, Ct)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        // The manager raises a request for one engineer; it fits the plan and, with no approval chain set up, is approved.
        using HttpClient lead = await factory.CreateScopedUserClientAsync(
            Email("lead"), manager, (Permissions.Workforce.RequestView, DataScope.Own), (Permissions.Workforce.RequestManage, DataScope.Own));
        JsonElement request = await OkAsync(await lead.PostAsJsonAsync("/api/v1/workforce/requests", new
        {
            Details = new
            {
                Title = "Backend Engineer",
                DepartmentId = department,
                Positions = 1,
                Skills = new[] { "C#", "SQL" },
                MinExperienceYears = 3m,
                MaxExperienceYears = 8m,
                EmploymentTypeCode = "FULL_TIME",
                WorkModeCode = "HYBRID",
                SalaryMin = 1_000_000m,
                SalaryMax = 1_500_000m,
                Budget = 1_500_000m,
                Priority = "High",
                RequiredBy = Iso(new DateOnly(year, 12, 31)),
                Reason = "Expansion",
                Justification = "New product line",
            },
            Submit = true,
        }, Ct));
        request.GetProperty("number").GetString().ShouldStartWith($"MPR-{year}-");
        request.GetProperty("status").GetString().ShouldBe("Approved");
        request.GetProperty("planFit").GetProperty("within").GetBoolean().ShouldBeTrue();
        request.GetProperty("planFit").GetProperty("remaining").GetInt32().ShouldBe(2);
        Guid requestId = request.GetProperty("id").GetGuid();

        // Approval opens a requisition for recruitment.
        await factory.DeliverEventsAsync();
        JsonElement requisition = (await admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/recruitment/requisitions?departmentId={department}", Ct))!
            .Single(row => row.TryGetProperty("manpowerRequestId", out JsonElement source) && source.GetGuid() == requestId);
        requisition.GetProperty("status").GetString().ShouldBe("Open");
        requisition.GetProperty("hiringManagerEmployeeId").GetGuid().ShouldBe(manager);
        Guid requisitionId = requisition.GetProperty("id").GetGuid();

        // A recruiter adds a candidate, takes them straight to an accepted offer, and hires.
        JsonElement added = await OkAsync(await admin.PostAsJsonAsync("/api/v1/recruitment/candidates", new { Details = Candidate("Meera"), Source = "Direct", RequisitionId = requisitionId }, Ct));
        Guid applicationId = (await admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/recruitment/applications?requisitionId={requisitionId}", Ct))!
            .Single(row => row.GetProperty("candidateId").GetGuid() == added.GetProperty("candidate").GetProperty("id").GetGuid()).GetProperty("id").GetGuid();
        Guid offerId = await AcceptedOfferAsync(admin, applicationId, Today);
        JsonElement hired = await OkAsync(await admin.PostAsJsonAsync($"/api/v1/recruitment/offers/{offerId}/hire", new { }, Ct));
        hired.GetProperty("requisitionFilled").GetBoolean().ShouldBeTrue();

        // Filling the requisition fulfils the manpower request.
        await factory.DeliverEventsAsync();
        (await lead.GetFromJsonAsync<JsonElement>($"/api/v1/workforce/requests/{requestId}", Ct)).GetProperty("status").GetString().ShouldBe("Fulfilled");

        JsonElement dashboard = await admin.GetFromJsonAsync<JsonElement>("/api/v1/workforce/dashboard", Ct);
        dashboard.GetProperty("departments").EnumerateArray().ShouldContain(row => row.GetProperty("departmentId").GetGuid() == department);
    }

    [Fact]
    public async Task A_request_beyond_the_plan_is_flagged_and_only_its_requester_or_scope_sees_it()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid department = await CoreHrApi.DepartmentAsync(admin);
        Guid requester = await CoreHrApi.HireAsync(admin, department);
        Guid stranger = await CoreHrApi.HireAsync(admin, await CoreHrApi.DepartmentAsync(admin));

        using HttpClient lead = await factory.CreateScopedUserClientAsync(Email("lead2"), requester, (Permissions.Workforce.RequestManage, DataScope.Own));
        JsonElement draft = await OkAsync(await lead.PostAsJsonAsync("/api/v1/workforce/requests", new
        {
            Details = new { Title = "Analyst", DepartmentId = department, Positions = 4, EmploymentTypeCode = "FULL_TIME", WorkModeCode = "OFFICE", Priority = "Low", Reason = "NewRole" },
            Submit = false,
        }, Ct));
        draft.GetProperty("status").GetString().ShouldBe("Draft");
        draft.GetProperty("canEdit").GetBoolean().ShouldBeTrue();
        Guid id = draft.GetProperty("id").GetGuid();

        using HttpClient other = await factory.CreateScopedUserClientAsync(Email("other"), stranger, (Permissions.Workforce.RequestView, DataScope.Own), (Permissions.Workforce.RequestManage, DataScope.Own));
        (await other.GetAsync($"/api/v1/workforce/requests/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.GetFromJsonAsync<JsonElement[]>("/api/v1/workforce/requests", Ct))!.ShouldNotContain(row => row.GetProperty("id").GetGuid() == id);
        (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/workforce/requests", Ct))!.ShouldContain(row => row.GetProperty("id").GetGuid() == id);

        (await OkAsync(await lead.PostAsync($"/api/v1/workforce/requests/{id}/cancel", null, Ct))).GetProperty("status").GetString().ShouldBe("Cancelled");
    }

    [Fact]
    public async Task The_pipeline_runs_from_referral_and_interviews_to_a_hire_with_onboarding_and_a_referral_bonus()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid department = await CoreHrApi.DepartmentAsync(admin);
        Guid manager = await CoreHrApi.HireAsync(admin, department);
        Guid referrer = await CoreHrApi.HireAsync(admin, department);
        Guid panelist = await CoreHrApi.HireAsync(admin, department);
        await OkAsync(await admin.PutAsJsonAsync("/api/v1/recruitment/settings", new
        {
            ReferralBonus = 20000m,
            ReferralBonusAfterMonths = 0,
            OfferValidDays = 7,
            Currency = "INR",
            ScorecardCriteria = new[] { "Technical skills", "Communication" },
        }, Ct));

        JsonElement requisition = await OkAsync(await admin.PostAsJsonAsync("/api/v1/recruitment/requisitions", Requisition(department, openings: 1, hiringManager: manager), Ct));
        Guid requisitionId = requisition.GetProperty("id").GetGuid();
        requisition.GetProperty("number").GetString().ShouldStartWith($"REQ-{Today.Year}-");

        // Posted on the internal job board, where an employee refers a friend.
        JsonElement posting = await OkAsync(await admin.PostAsJsonAsync("/api/v1/recruitment/postings", new { RequisitionId = requisitionId, Title = "Backend Engineer", Description = "Join the platform team.", Internal = true, External = true }, Ct));
        await OkAsync(await admin.PostAsync($"/api/v1/recruitment/postings/{posting.GetProperty("id").GetGuid()}/publish", null, Ct));
        using HttpClient employee = await factory.CreateScopedUserClientAsync(Email("referrer"), referrer, (Permissions.Self.ReferralCreate, DataScope.Own));
        (await employee.GetFromJsonAsync<JsonElement[]>("/api/v1/me/jobs", Ct))!.ShouldContain(job => job.GetProperty("requisitionId").GetGuid() == requisitionId);
        string friendEmail = Email("friend");
        JsonElement[] referrals = (await OkAsync(await employee.PostAsJsonAsync("/api/v1/me/referrals", new
        {
            Candidate = Candidate("Kiran", friendEmail, "+91 98450 12345", ["C#", "SQL", "Azure"]),
            RequisitionId = requisitionId,
            Note = "Worked with Kiran for three years.",
        }, Ct))).EnumerateArray().ToArray();
        referrals.ShouldHaveSingleItem().GetProperty("status").GetString().ShouldBe("InProcess");
        (await employee.PostAsJsonAsync("/api/v1/me/referrals", new { Candidate = Candidate("Kiran", friendEmail), RequisitionId = requisitionId }, Ct)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        // The same person entered again (by mobile) is found, not duplicated; an agency candidate joins the pipeline too.
        JsonElement agencies = await OkAsync(await admin.PostAsJsonAsync("/api/v1/recruitment/agencies", new { Name = "TalentBridge " + CoreHrApi.Unique("A"), FeePercent = 8.33m }, Ct));
        Guid agency = agencies.EnumerateArray().First(row => row.GetProperty("name").GetString()!.StartsWith("TalentBridge", StringComparison.Ordinal)).GetProperty("id").GetGuid();
        JsonElement again = await OkAsync(await admin.PostAsJsonAsync("/api/v1/recruitment/candidates", new { Details = Candidate("Kiran", Email("kiran-alt"), "919845012345"), Source = "Direct" }, Ct));
        again.GetProperty("candidate").GetProperty("email").GetString().ShouldBe(friendEmail);
        await OkAsync(await admin.PostAsJsonAsync("/api/v1/recruitment/candidates", new { Details = Candidate("Ravi", skills: ["Java"], experience: 1), Source = "Agency", AgencyId = agency, RequisitionId = requisitionId }, Ct));

        JsonElement[] pipeline = (await admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/recruitment/applications?requisitionId={requisitionId}", Ct))!;
        pipeline.Length.ShouldBe(2);
        JsonElement kiran = pipeline.Single(row => row.GetProperty("candidateEmail").GetString() == friendEmail);
        JsonElement ravi = pipeline.Single(row => row.GetProperty("candidateEmail").GetString() != friendEmail);
        kiran.GetProperty("matchScore").GetInt32().ShouldBe(100);
        ravi.GetProperty("matchScore").GetInt32().ShouldBeLessThan(kiran.GetProperty("matchScore").GetInt32());
        Guid applicationId = kiran.GetProperty("id").GetGuid();

        // Screened, then interviewed by a panel member who gives feedback from self-service.
        await OkAsync(await admin.PostAsJsonAsync($"/api/v1/recruitment/applications/{applicationId}/move", new { Stage = "Shortlisted", Note = "Strong profile" }, Ct));
        JsonElement scheduled = await OkAsync(await admin.PostAsJsonAsync($"/api/v1/recruitment/applications/{applicationId}/interviews", new
        {
            Round = "Technical 1",
            ScheduledAt = DateTime.UtcNow.AddDays(2),
            DurationMinutes = 60,
            Mode = "Video",
            Location = "https://meet.example/abc",
            Panel = new[] { panelist },
        }, Ct));
        scheduled.GetProperty("application").GetProperty("stage").GetString().ShouldBe("Interview");
        Guid interviewId = scheduled.GetProperty("interviews")[0].GetProperty("id").GetGuid();

        using HttpClient panel = await factory.CreateScopedUserClientAsync(Email("panel"), panelist, (Permissions.Self.InterviewFeedback, DataScope.Own));
        (await panel.GetFromJsonAsync<JsonElement[]>("/api/v1/me/interviews", Ct))!.ShouldContain(row => row.GetProperty("id").GetGuid() == interviewId);
        JsonElement mine = await panel.GetFromJsonAsync<JsonElement>($"/api/v1/me/interviews/{interviewId}", Ct);
        mine.GetProperty("criteria").GetArrayLength().ShouldBe(2);
        mine.GetProperty("candidate").TryGetProperty("expectedCtc", out _).ShouldBeFalse();
        (await panel.PutAsJsonAsync($"/api/v1/me/interviews/{interviewId}/scorecard", new { Ratings = new Dictionary<string, int> { ["Technical skills"] = 6 }, Recommendation = "Yes" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        JsonElement feedback = await OkAsync(await panel.PutAsJsonAsync($"/api/v1/me/interviews/{interviewId}/scorecard", new
        {
            Ratings = new Dictionary<string, int> { ["Technical skills"] = 5, ["Communication"] = 4 },
            Recommendation = "StrongYes",
            Comments = "Excellent system design.",
        }, Ct));
        feedback.GetProperty("myScorecard").GetProperty("overallRating").GetDecimal().ShouldBe(4.5m);
        using HttpClient outsider = await factory.CreateScopedUserClientAsync(Email("outsider"), await CoreHrApi.HireAsync(admin, department), (Permissions.Self.InterviewFeedback, DataScope.Own));
        (await outsider.GetAsync($"/api/v1/me/interviews/{interviewId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await outsider.GetAsync("/api/v1/recruitment/requisitions", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Background verification clears, the offer is approved, sent and accepted, and the candidate is hired.
        Guid candidateId = kiran.GetProperty("candidateId").GetGuid();
        JsonElement checks = await OkAsync(await admin.PostAsJsonAsync("/api/v1/recruitment/background-checks", new { CandidateId = candidateId, ApplicationId = applicationId, Vendor = "VerifyCo", Checks = new[] { "Identity", "Employment" } }, Ct));
        Guid checkId = checks.GetProperty("backgroundChecks")[0].GetProperty("id").GetGuid();
        await OkAsync(await admin.PostAsJsonAsync($"/api/v1/recruitment/background-checks/{checkId}/result", new { Status = "Clear" }, Ct));

        Guid offerId = await AcceptedOfferAsync(admin, applicationId, Today);
        JsonElement hire = await OkAsync(await admin.PostAsJsonAsync($"/api/v1/recruitment/offers/{offerId}/hire", new { }, Ct));
        Guid newEmployee = hire.GetProperty("employeeId").GetGuid();
        hire.GetProperty("requisitionFilled").GetBoolean().ShouldBeTrue();
        (await admin.PostAsJsonAsync($"/api/v1/recruitment/offers/{offerId}/hire", new { }, Ct)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        // The hire: an employee with the offered CTC, the other applicant closed, the requisition filled.
        JsonElement person = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/employees/{newEmployee}", Ct);
        person.ToString().ShouldContain(department.ToString());
        person.ToString().ShouldContain("Backend Engineer");
        JsonElement salary = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/payroll/employees/{newEmployee}/salary", Ct);
        salary.ToString().ShouldContain("1450000");
        JsonElement[] after = (await admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/recruitment/applications?requisitionId={requisitionId}", Ct))!;
        after.Single(row => row.GetProperty("id").GetGuid() == applicationId).GetProperty("stage").GetString().ShouldBe("Hired");
        after.Single(row => row.GetProperty("id").GetGuid() != applicationId).GetProperty("stage").GetString().ShouldBe("Rejected");
        (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/recruitment/requisitions/{requisitionId}", Ct)).GetProperty("status").GetString().ShouldBe("Filled");

        // Onboarding starts from the hire, with the manager's and the joiner's tasks assigned.
        await factory.DeliverEventsAsync();
        JsonElement onboarding = (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/onboarding/cases?open=true", Ct))!.Single(row => row.GetProperty("employeeId").GetGuid() == newEmployee);
        onboarding.GetProperty("managerEmployeeId").GetGuid().ShouldBe(manager);
        onboarding.GetProperty("tasks").GetInt32().ShouldBeGreaterThan(10);
        JsonElement view = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/onboarding/cases/{onboarding.GetProperty("id").GetGuid()}", Ct);
        view.GetProperty("tasks").EnumerateArray().ShouldContain(task => task.GetProperty("assigneeRole").GetString() == "Manager" && task.GetProperty("assigneeEmployeeId").GetGuid() == manager);
        view.GetProperty("tasks").EnumerateArray().ShouldContain(task => task.GetProperty("assigneeRole").GetString() == "Employee" && task.GetProperty("assigneeEmployeeId").GetGuid() == newEmployee);

        // The referral turned into a hire; with no waiting period the bonus goes to payroll on the next daily pass.
        JsonElement referral = (await employee.GetFromJsonAsync<JsonElement[]>("/api/v1/me/referrals", Ct))!.Single();
        referral.GetProperty("status").GetString().ShouldBe("Hired");
        referral.GetProperty("bonusAmount").GetDecimal().ShouldBe(20000m);
        int awarded = 0;
        await factory.RunAsWorkerAsync(PeopleOsApiFactory.SeededTenantId, async services => awarded = await services.GetRequiredService<OfferService>().AwardDueReferralBonusesAsync(Ct));
        awarded.ShouldBeGreaterThanOrEqualTo(1);
        (await employee.GetFromJsonAsync<JsonElement[]>("/api/v1/me/referrals", Ct))!.Single().GetProperty("status").GetString().ShouldBe("BonusAwarded");

        // Analytics count the hire, the accepted offer and the referral source.
        JsonElement analytics = await admin.GetFromJsonAsync<JsonElement>("/api/v1/recruitment/analytics", Ct);
        analytics.GetProperty("hires").GetInt32().ShouldBeGreaterThanOrEqualTo(1);
        analytics.GetProperty("offersAccepted").GetInt32().ShouldBeGreaterThanOrEqualTo(1);
        analytics.GetProperty("sources").EnumerateArray().ShouldContain(row => row.GetProperty("source").GetString() == "Referral" && row.GetProperty("hired").GetInt32() >= 1);
    }

    [Fact]
    public async Task An_offer_cannot_skip_its_steps_and_a_declined_offer_closes_the_application()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid department = await CoreHrApi.DepartmentAsync(admin);
        JsonElement requisition = await OkAsync(await admin.PostAsJsonAsync("/api/v1/recruitment/requisitions", Requisition(department, openings: 2), Ct));
        Guid requisitionId = requisition.GetProperty("id").GetGuid();
        JsonElement added = await OkAsync(await admin.PostAsJsonAsync("/api/v1/recruitment/candidates", new { Details = Candidate("Neha"), Source = "JobBoard", RequisitionId = requisitionId }, Ct));
        Guid applicationId = added.GetProperty("applications")[0].GetProperty("id").GetGuid();

        (await admin.PostAsJsonAsync($"/api/v1/recruitment/applications/{applicationId}/move", new { Stage = "Offer" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        JsonElement draft = await OkAsync(await admin.PostAsJsonAsync("/api/v1/recruitment/offers", new
        {
            ApplicationId = applicationId,
            Terms = new { AnnualCtc = 1_700_000m, Currency = "", JoiningDate = Iso(Today.AddDays(30)), Designation = "Senior Engineer" },
            Submit = false,
        }, Ct));
        draft.GetProperty("withinBudget").GetBoolean().ShouldBeFalse();
        draft.GetProperty("currency").GetString().ShouldBe("INR");
        Guid offerId = draft.GetProperty("id").GetGuid();
        (await admin.PostAsync($"/api/v1/recruitment/offers/{offerId}/send", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await admin.PostAsJsonAsync("/api/v1/recruitment/offers", new { ApplicationId = applicationId, Terms = new { AnnualCtc = 1m, Currency = "INR", JoiningDate = Iso(Today.AddDays(30)), Designation = "X" }, Submit = false }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        await OkAsync(await admin.PostAsync($"/api/v1/recruitment/offers/{offerId}/submit", null, Ct));
        await OkAsync(await admin.PostAsync($"/api/v1/recruitment/offers/{offerId}/send", null, Ct));
        (await admin.PostAsJsonAsync($"/api/v1/recruitment/offers/{offerId}/hire", new { }, Ct)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        JsonElement declined = await OkAsync(await admin.PostAsJsonAsync($"/api/v1/recruitment/offers/{offerId}/respond", new { Accepted = false, Reason = "Counter-offer" }, Ct));
        declined.GetProperty("status").GetString().ShouldBe("Declined");

        JsonElement application = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/recruitment/applications/{applicationId}", Ct);
        application.GetProperty("application").GetProperty("stage").GetString().ShouldBe("Rejected");
        application.GetProperty("history").GetArrayLength().ShouldBeGreaterThanOrEqualTo(3);

        JsonElement analytics = await admin.GetFromJsonAsync<JsonElement>("/api/v1/recruitment/analytics", Ct);
        analytics.GetProperty("offersDeclined").GetInt32().ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Onboarding_assigns_owners_completes_when_required_tasks_are_done_and_reminds_overdue_owners()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid department = await CoreHrApi.DepartmentAsync(admin);
        Guid manager = await CoreHrApi.HireAsync(admin, department);
        Guid itOwner = await CoreHrApi.HireAsync(admin, department);
        await factory.DeliverEventsAsync();
        await OkAsync(await admin.PutAsJsonAsync("/api/v1/onboarding/owners", new { HrOwnerEmployeeId = manager, ItOwnerEmployeeId = itOwner, AdminOwnerEmployeeId = itOwner }, Ct));

        // A department-specific checklist beats the default for that department.
        JsonElement[] templates = (await OkAsync(await admin.PostAsJsonAsync("/api/v1/onboarding/templates", new
        {
            Details = new
            {
                Name = "Engineering " + CoreHrApi.Unique("T"),
                DepartmentId = department,
                IsDefault = false,
                Tasks = new object[]
                {
                    new { Title = "Laptop", Category = "Laptop", Assignee = "It", DueOffsetDays = -1 },
                    new { Title = "Sign policies", Category = "Documents", Assignee = "Employee", DueOffsetDays = 0 },
                    new { Title = "Team lunch", Category = "ManagerIntroduction", Assignee = "Manager", DueOffsetDays = 2, Required = false },
                },
            },
        }, Ct))).EnumerateArray().ToArray();
        templates.ShouldContain(row => row.GetProperty("isDefault").GetBoolean());

        // A joiner who started in 2024, so everything is already overdue.
        Guid joiner = await CoreHrApi.HireAsync(admin, department, manager);
        await factory.DeliverEventsAsync();
        JsonElement summary = (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/onboarding/cases?open=true", Ct))!.Single(row => row.GetProperty("employeeId").GetGuid() == joiner);
        summary.GetProperty("templateName").GetString()!.ShouldStartWith("Engineering");
        Guid caseId = summary.GetProperty("id").GetGuid();
        JsonElement view = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/onboarding/cases/{caseId}", Ct);
        JsonElement[] tasks = [.. view.GetProperty("tasks").EnumerateArray()];
        tasks.Length.ShouldBe(3);
        Guid laptop = tasks.Single(task => task.GetProperty("title").GetString() == "Laptop").GetProperty("id").GetGuid();
        Guid policies = tasks.Single(task => task.GetProperty("title").GetString() == "Sign policies").GetProperty("id").GetGuid();
        Guid lunch = tasks.Single(task => task.GetProperty("title").GetString() == "Team lunch").GetProperty("id").GetGuid();
        tasks.Single(task => task.GetProperty("title").GetString() == "Laptop").GetProperty("assigneeEmployeeId").GetGuid().ShouldBe(itOwner);

        int reminded = 0;
        await factory.RunAsWorkerAsync(PeopleOsApiFactory.SeededTenantId, async services => reminded = await services.GetRequiredService<OnboardingService>().DailyAsync(Ct));
        reminded.ShouldBeGreaterThanOrEqualTo(2);

        // The IT owner sees and closes their task; the joiner signs; a required task cannot be skipped.
        using HttpClient it = await factory.CreateScopedUserClientAsync(Email("it"), itOwner, (Permissions.Self.OnboardingView, DataScope.Own));
        (await it.GetFromJsonAsync<JsonElement[]>("/api/v1/me/onboarding/tasks", Ct))!.ShouldContain(task => task.GetProperty("id").GetGuid() == laptop);
        (await it.PostAsJsonAsync($"/api/v1/onboarding/tasks/{policies}/finish", new { Skipped = false }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await OkAsync(await it.PostAsJsonAsync($"/api/v1/onboarding/tasks/{laptop}/finish", new { Skipped = false, Notes = "ThinkPad T14 issued" }, Ct));

        using HttpClient self = await factory.CreateScopedUserClientAsync(Email("joiner"), joiner, (Permissions.Self.OnboardingView, DataScope.Own));
        JsonElement my = await self.GetFromJsonAsync<JsonElement>("/api/v1/me/onboarding", Ct);
        my.GetProperty("case").GetProperty("id").GetGuid().ShouldBe(caseId);
        (await self.PostAsJsonAsync($"/api/v1/onboarding/tasks/{policies}/finish", new { Skipped = true }, Ct)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        await OkAsync(await self.PostAsJsonAsync($"/api/v1/onboarding/tasks/{policies}/finish", new { Skipped = false }, Ct));

        // Only the optional task is left, so the case is complete.
        JsonElement done = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/onboarding/cases/{caseId}", Ct);
        done.GetProperty("case").GetProperty("status").GetString().ShouldBe("Completed");
        done.GetProperty("tasks").EnumerateArray().Single(task => task.GetProperty("id").GetGuid() == lunch).GetProperty("status").GetString().ShouldBe("Pending");

        // Reopening a required task reopens the case.
        await OkAsync(await admin.PostAsync($"/api/v1/onboarding/tasks/{laptop}/reopen", null, Ct));
        (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/onboarding/cases/{caseId}", Ct)).GetProperty("case").GetProperty("status").GetString().ShouldBe("InProgress");

        JsonElement dashboard = await admin.GetFromJsonAsync<JsonElement>("/api/v1/onboarding/dashboard", Ct);
        dashboard.GetProperty("open").GetInt32().ShouldBeGreaterThanOrEqualTo(1);
    }

    /// <summary>Drafts an offer joining on <paramref name="joining"/>, approves it (no chain configured), sends it and records acceptance.</summary>
    private static async Task<Guid> AcceptedOfferAsync(HttpClient admin, Guid applicationId, DateOnly joining)
    {
        JsonElement offer = await OkAsync(await admin.PostAsJsonAsync("/api/v1/recruitment/offers", new
        {
            ApplicationId = applicationId,
            Terms = new { AnnualCtc = 1_450_000m, Currency = "INR", JoiningDate = Iso(joining), Designation = "Backend Engineer" },
            Submit = true,
        }, Ct));
        offer.GetProperty("status").GetString().ShouldBe("Approved");
        offer.GetProperty("number").GetString().ShouldStartWith($"OFF-{Today.Year}-");
        Guid id = offer.GetProperty("id").GetGuid();
        await OkAsync(await admin.PostAsync($"/api/v1/recruitment/offers/{id}/send", null, Ct));
        (await OkAsync(await admin.PostAsJsonAsync($"/api/v1/recruitment/offers/{id}/respond", new { Accepted = true }, Ct))).GetProperty("status").GetString().ShouldBe("Accepted");
        return id;
    }
}
