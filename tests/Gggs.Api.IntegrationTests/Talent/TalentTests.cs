using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Gggs.Modules.Learning.Application;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using static Gggs.Api.IntegrationTests.DeliveryApi;
using static Gggs.Api.IntegrationTests.PlatformServicesApi;

namespace Gggs.Api.IntegrationTests;

/// <summary>
/// M9 end to end: cascading goals with key results and check-ins, a review cycle from launch through
/// self, 360 and manager reviews to calibration and release, an improvement plan, learning with joiner
/// assignments and certificate renewal, and career paths, promotions, succession and the 9-box.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class TalentTests(PeopleOsApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly (string, DataScope)[] Leader =
    [
        (Permissions.Performance.GoalManage, DataScope.DirectReports),
        (Permissions.Performance.GoalManageOwn, DataScope.Own),
        (Permissions.Performance.ReviewView, DataScope.DirectReports),
        (Permissions.Performance.ReviewManageAsManager, DataScope.DirectReports),
        (Permissions.Performance.ReviewManageOwn, DataScope.Own),
        (Permissions.Performance.PipView, DataScope.DirectReports),
        (Permissions.Performance.PipManage, DataScope.DirectReports),
        (Permissions.Career.PromotionView, DataScope.DirectReports),
        (Permissions.Career.PromotionManage, DataScope.DirectReports),
        (Permissions.Career.TalentReviewManage, DataScope.DirectReports),
        (Permissions.Self.FeedbackGive, DataScope.Own),
    ];

    private static readonly (string, DataScope)[] Member =
    [
        (Permissions.Performance.GoalManageOwn, DataScope.Own),
        (Permissions.Performance.ReviewManageOwn, DataScope.Own),
        (Permissions.Self.FeedbackGive, DataScope.Own),
        (Permissions.Self.LearningView, DataScope.Own),
        (Permissions.Learning.EnrollmentManageOwn, DataScope.Own),
        (Permissions.Self.CareerView, DataScope.Own),
    ];

    /// <summary>A department with a manager and two reports, each signed in with their own client.</summary>
    private async Task<(HttpClient Admin, Guid Department, Guid Manager, Guid Report, Guid Peer, HttpClient Lead, HttpClient Self, HttpClient Colleague)> TeamAsync()
    {
        HttpClient admin = await factory.CreateAdminClientAsync();
        Guid department = await CoreHrApi.DepartmentAsync(admin);
        Guid manager = await CoreHrApi.HireAsync(admin, department);
        Guid report = await CoreHrApi.HireAsync(admin, department, manager);
        Guid peer = await CoreHrApi.HireAsync(admin, department, manager);
        HttpClient lead = await factory.CreateScopedUserClientAsync(Email("lead"), manager, Leader);
        HttpClient self = await factory.CreateScopedUserClientAsync(Email("report"), report, Member);
        HttpClient colleague = await factory.CreateScopedUserClientAsync(Email("peer"), peer, Member);
        return (admin, department, manager, report, peer, lead, self, colleague);
    }

    [Fact]
    public async Task Goals_cascade_from_the_company_and_key_results_drive_progress()
    {
        (HttpClient admin, Guid department, _, Guid report, _, HttpClient lead, HttpClient self, HttpClient colleague) = await TeamAsync();
        using (admin)
        using (lead)
        using (self)
        using (colleague)
        {
            string start = Iso(Today.AddMonths(-1));
            string due = Iso(Today.AddMonths(5));
            JsonElement company = await OkAsync(await admin.PostAsJsonAsync("/api/v1/goals", new
            {
                Details = new { Title = "Grow revenue 20%", Type = "Okr", Level = "Company", StartDate = start, DueDate = due, Weight = 0 },
                Activate = true,
            }, Ct));
            Guid companyId = company.GetProperty("goal").GetProperty("id").GetGuid();
            (await lead.PostAsJsonAsync("/api/v1/goals", new { Details = new { Title = "Company goal by a lead", Type = "Okr", Level = "Company", StartDate = start, DueDate = due, Weight = 0 }, Activate = true }, Ct))
                .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

            // The manager sets their report an aligned goal with two key results.
            JsonElement goal = await OkAsync(await lead.PostAsJsonAsync("/api/v1/goals", new
            {
                Details = new { Title = "Ship the billing API", Type = "Okr", Level = "Individual", OwnerEmployeeId = report, DepartmentId = department, ParentGoalId = companyId, StartDate = start, DueDate = due, Weight = 60 },
                KeyResults = new object[]
                {
                    new { Title = "Endpoints live", MetricType = "Number", StartValue = 0, TargetValue = 10, Unit = "endpoints", Weight = 50 },
                    new { Title = "Security review passed", MetricType = "Milestone", StartValue = 0, TargetValue = 1, Weight = 50 },
                },
                Activate = true,
            }, Ct));
            Guid goalId = goal.GetProperty("goal").GetProperty("id").GetGuid();
            goal.GetProperty("goal").GetProperty("parentTitle").GetString().ShouldBe("Grow revenue 20%");
            JsonElement[] results = [.. goal.GetProperty("keyResults").EnumerateArray()];

            // Weights over 100% are refused.
            (await self.PostAsJsonAsync("/api/v1/goals", new { Details = new { Title = "Too much", Type = "Kpi", Level = "Individual", StartDate = start, DueDate = due, Weight = 50 }, Activate = true }, Ct))
                .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

            // The report checks in: 5 of 10 endpoints and the review not yet passed is 25%.
            JsonElement checkedIn = await OkAsync(await self.PostAsJsonAsync($"/api/v1/goals/{goalId}/check-ins", new
            {
                Confidence = "AtRisk",
                Note = "Halfway through the endpoints.",
                Values = new[] { new { KeyResultId = results[0].GetProperty("id").GetGuid(), Value = 5m }, new { KeyResultId = results[1].GetProperty("id").GetGuid(), Value = 0m } },
            }, Ct));
            checkedIn.GetProperty("goal").GetProperty("progress").GetDecimal().ShouldBe(25m);
            checkedIn.GetProperty("checkIns").GetArrayLength().ShouldBe(1);

            // A colleague sees the company goal but not the report's own goal.
            (await colleague.GetFromJsonAsync<JsonElement[]>("/api/v1/goals", Ct))!.ShouldContain(row => row.GetProperty("id").GetGuid() == companyId);
            (await colleague.GetAsync($"/api/v1/goals/{goalId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await self.GetFromJsonAsync<JsonElement[]>("/api/v1/me/goals", Ct))!.ShouldContain(row => row.GetProperty("id").GetGuid() == goalId);
            (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/goals/{companyId}", Ct)).GetProperty("children").GetArrayLength().ShouldBeGreaterThanOrEqualTo(1);
        }
    }

    [Fact]
    public async Task A_review_cycle_runs_from_launch_through_feedback_calibration_and_release_into_the_9_box()
    {
        (HttpClient admin, Guid department, Guid manager, Guid report, Guid peer, HttpClient lead, HttpClient self, HttpClient colleague) = await TeamAsync();
        using (admin)
        using (lead)
        using (self)
        using (colleague)
        {
            await OkAsync(await lead.PostAsJsonAsync("/api/v1/goals", new
            {
                Details = new { Title = "Close 40 tickets", Type = "Kpi", Level = "Individual", OwnerEmployeeId = report, StartDate = Iso(Today.AddMonths(-2)), DueDate = Iso(Today.AddMonths(1)), Weight = 100 },
                Activate = true,
            }, Ct));
            Guid goalId = (await self.GetFromJsonAsync<JsonElement[]>("/api/v1/me/goals", Ct))!.Single().GetProperty("id").GetGuid();
            await OkAsync(await self.PostAsJsonAsync($"/api/v1/goals/{goalId}/check-ins", new { Progress = 75m, Confidence = "OnTrack" }, Ct));

            JsonElement cycle = await OkAsync(await admin.PostAsJsonAsync("/api/v1/performance/cycles", new
            {
                Name = "H2 review " + CoreHrApi.Unique("C"),
                Type = "HalfYearly",
                PeriodStart = Iso(Today.AddMonths(-6)),
                PeriodEnd = Iso(Today.AddDays(10)),
                SelfReviewDue = Iso(Today.AddDays(7)),
                ManagerReviewDue = Iso(Today.AddDays(14)),
                Competencies = new[] { "Delivery", "Collaboration" },
                GoalWeight = 50,
                PeerFeedback = true,
                DepartmentIds = new[] { department },
            }, Ct));
            Guid cycleId = cycle.GetProperty("id").GetGuid();
            JsonElement launched = await OkAsync(await admin.PostAsync($"/api/v1/performance/cycles/{cycleId}/launch", null, Ct));
            launched.GetProperty("reviews").GetInt32().ShouldBe(3);

            Guid reviewId = (await self.GetFromJsonAsync<JsonElement[]>("/api/v1/me/reviews", Ct))!.Single(row => row.GetProperty("cycleId").GetGuid() == cycleId).GetProperty("id").GetGuid();
            (await lead.GetFromJsonAsync<JsonElement[]>("/api/v1/me/reviews/team", Ct))!.ShouldContain(row => row.GetProperty("id").GetGuid() == reviewId);

            // The report asks a peer for feedback and submits their self-review; the peer responds.
            await OkAsync(await self.PostAsJsonAsync($"/api/v1/performance/reviews/{reviewId}/feedback", new { ReviewerIds = new[] { peer }, Relationship = "Peer" }, Ct));
            (await self.PutAsJsonAsync($"/api/v1/performance/reviews/{reviewId}/self", new { Input = new { Ratings = new Dictionary<string, int> { ["Charisma"] = 4 } }, Submit = true }, Ct))
                .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
            JsonElement afterSelf = await OkAsync(await self.PutAsJsonAsync($"/api/v1/performance/reviews/{reviewId}/self", new
            {
                Input = new { Ratings = new Dictionary<string, int> { ["Delivery"] = 4, ["Collaboration"] = 4 }, Strengths = "Shipped on time", Overall = 4 },
                Submit = true,
            }, Ct));
            afterSelf.GetProperty("review").GetProperty("status").GetString().ShouldBe("SelfSubmitted");
            afterSelf.GetProperty("feedback").GetArrayLength().ShouldBe(0);

            JsonElement request = (await colleague.GetFromJsonAsync<JsonElement[]>("/api/v1/me/feedback", Ct))!.Single(row => row.GetProperty("reviewId").GetGuid() == reviewId);
            request.GetProperty("competencies").GetArrayLength().ShouldBe(2);
            await OkAsync(await colleague.PutAsJsonAsync($"/api/v1/me/feedback/{request.GetProperty("id").GetGuid()}", new
            {
                Ratings = new Dictionary<string, int> { ["Collaboration"] = 5 },
                Strengths = "Always helps the team",
                Improvements = "Write more docs",
            }, Ct));

            // The manager sees the goal evidence and the peer's feedback, and submits.
            JsonElement managerView = await lead.GetFromJsonAsync<JsonElement>($"/api/v1/performance/reviews/{reviewId}", Ct);
            managerView.GetProperty("canEditManager").GetBoolean().ShouldBeTrue();
            managerView.GetProperty("evidence").GetProperty("goalAchievement").GetDecimal().ShouldBe(75m);
            managerView.GetProperty("evidence").GetProperty("goalRating").GetDecimal().ShouldBe(4m);
            managerView.GetProperty("feedback").EnumerateArray().Single().GetProperty("reviewerName").GetString().ShouldNotBeNull();
            JsonElement submitted = await OkAsync(await lead.PutAsJsonAsync($"/api/v1/performance/reviews/{reviewId}/manager", new
            {
                Input = new { Ratings = new Dictionary<string, int> { ["Delivery"] = 3, ["Collaboration"] = 5 }, Comments = "Strong half", Overall = 4 },
                Submit = true,
            }, Ct));
            submitted.GetProperty("review").GetProperty("score").GetDecimal().ShouldBe(4m);

            // Calibration lowers the rating; release gives the employee the calibrated rating and anonymous feedback.
            JsonElement calibration = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/performance/cycles/{cycleId}/calibration", Ct);
            calibration.GetProperty("awaitingManager").GetInt32().ShouldBe(2);
            calibration.GetProperty("distribution").GetArrayLength().ShouldBe(5);
            await OkAsync(await admin.PostAsJsonAsync($"/api/v1/performance/reviews/{reviewId}/calibrate", new { Rating = 3, Note = "Aligned with peers" }, Ct));
            (await self.PostAsJsonAsync($"/api/v1/performance/reviews/{reviewId}/acknowledge", new { Comment = "Too early" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
            await OkAsync(await admin.PostAsync($"/api/v1/performance/cycles/{cycleId}/release", null, Ct));

            JsonElement released = await self.GetFromJsonAsync<JsonElement>($"/api/v1/performance/reviews/{reviewId}", Ct);
            released.GetProperty("review").GetProperty("finalRating").GetInt32().ShouldBe(3);
            released.GetProperty("review").GetProperty("finalLabel").GetString().ShouldBe("Meets expectations");
            JsonElement anonymous = released.GetProperty("feedback").EnumerateArray().Single();
            anonymous.TryGetProperty("reviewerName", out _).ShouldBeFalse();
            await OkAsync(await self.PostAsJsonAsync($"/api/v1/performance/reviews/{reviewId}/acknowledge", new { Comment = "Thanks" }, Ct));

            // The 9-box: the manager assesses potential and the released rating places the report.
            JsonElement nineBox = await OkAsync(await lead.PostAsJsonAsync("/api/v1/career/talent-review/assessments", new { EmployeeId = report, Potential = "High", FlightRisk = "Low" }, Ct));
            JsonElement row = nineBox.GetProperty("people").EnumerateArray().Single(person => person.GetProperty("employeeId").GetGuid() == report);
            row.GetProperty("rating").GetInt32().ShouldBe(3);
            row.GetProperty("box").GetInt32().ShouldBe(8);
            row.GetProperty("boxLabel").GetString().ShouldBe("Emerging star");
            (await lead.PostAsJsonAsync("/api/v1/career/talent-review/assessments", new { EmployeeId = manager, Potential = "High", FlightRisk = "Low" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task An_improvement_plan_runs_to_an_outcome_and_only_the_manager_runs_it()
    {
        (HttpClient admin, _, _, Guid report, _, HttpClient lead, HttpClient self, HttpClient colleague) = await TeamAsync();
        using (admin)
        using (lead)
        using (self)
        using (colleague)
        {
            JsonElement plan = await OkAsync(await lead.PostAsJsonAsync("/api/v1/performance/pips", new
            {
                EmployeeId = report,
                Details = new
                {
                    Reason = "Missed delivery dates three sprints running.",
                    StartDate = Iso(Today),
                    EndDate = Iso(Today.AddDays(60)),
                    Objectives = new[] { new { Title = "Deliver sprint commitments", Measure = "90% of committed points" } },
                },
            }, Ct));
            Guid planId = plan.GetProperty("id").GetGuid();
            (await lead.PostAsJsonAsync("/api/v1/performance/pips", new { EmployeeId = report, Details = new { Reason = "Again", StartDate = Iso(Today), EndDate = Iso(Today.AddDays(30)), Objectives = new[] { new { Title = "X" } } } }, Ct))
                .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
            (await self.GetFromJsonAsync<JsonElement[]>("/api/v1/me/pips", Ct))!.ShouldBeEmpty();

            await OkAsync(await lead.PostAsync($"/api/v1/performance/pips/{planId}/start", null, Ct));
            await OkAsync(await lead.PostAsJsonAsync($"/api/v1/performance/pips/{planId}/notes", new { Text = "Week 2: back on track." }, Ct));
            await OkAsync(await lead.PostAsJsonAsync($"/api/v1/performance/pips/{planId}/extend", new { EndDate = Iso(Today.AddDays(90)) }, Ct));
            JsonElement closed = await OkAsync(await lead.PostAsJsonAsync($"/api/v1/performance/pips/{planId}/close", new
            {
                Succeeded = true,
                Outcome = "Commitments met for six sprints.",
                Objectives = new[] { new { Title = "Deliver sprint commitments", Measure = "90% of committed points", Met = true } },
            }, Ct));
            closed.GetProperty("status").GetString().ShouldBe("Succeeded");
            closed.GetProperty("extended").GetBoolean().ShouldBeTrue();
            closed.GetProperty("notes").GetArrayLength().ShouldBe(1);

            (await self.GetFromJsonAsync<JsonElement[]>("/api/v1/me/pips", Ct))!.ShouldHaveSingleItem();
            (await colleague.GetAsync($"/api/v1/performance/pips/{planId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task Joiners_get_mandatory_learning_and_lapsed_certificates_are_assigned_again()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid department = await CoreHrApi.DepartmentAsync(admin);
        Guid joiner = await CoreHrApi.HireAsync(admin, department);
        await factory.DeliverEventsAsync();

        JsonElement[] assigned = (await admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/learning/enrollments?employeeId={joiner}", Ct))!;
        assigned.Select(row => row.GetProperty("courseCode").GetString()).Order().ShouldBe(["COC", "INFOSEC", "POSH"]);
        assigned.ShouldAllBe(row => row.GetProperty("source").GetString() == "Joiner");

        using HttpClient self = await factory.CreateScopedUserClientAsync(Email("learner"), joiner, Member);
        JsonElement mine = await self.GetFromJsonAsync<JsonElement>("/api/v1/me/learning", Ct);
        mine.GetProperty("active").GetArrayLength().ShouldBe(3);

        // Completing the code of conduct a year and a day ago: the certificate has already lapsed.
        Guid coc = assigned.Single(row => row.GetProperty("courseCode").GetString() == "COC").GetProperty("id").GetGuid();
        (await self.PostAsJsonAsync($"/api/v1/learning/enrollments/{coc}/progress", new { Progress = 100 }, Ct)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        await OkAsync(await self.PostAsJsonAsync($"/api/v1/learning/enrollments/{coc}/progress", new { Progress = 50 }, Ct));
        JsonElement completed = await OkAsync(await self.PostAsJsonAsync($"/api/v1/learning/enrollments/{coc}/complete", new { CompletedOn = Iso(Today.AddYears(-1).AddDays(-1)), Score = 92 }, Ct));
        completed.GetProperty("certificateNumber").GetString()!.ShouldStartWith("COC-");
        completed.GetProperty("certificateExpiresOn").GetString().ShouldBe(Iso(Today.AddDays(-1)));
        await OkAsync(await self.PostAsJsonAsync($"/api/v1/learning/enrollments/{coc}/rate", new { Rating = 5, Feedback = "Clear and short" }, Ct));

        // Self-enrollment in an optional course, once.
        Guid privacy = (await self.GetFromJsonAsync<JsonElement[]>("/api/v1/me/learning/catalog", Ct))!
            .Single(row => row.GetProperty("course").GetProperty("code").GetString() == "DATAPRIV").GetProperty("course").GetProperty("id").GetGuid();
        await OkAsync(await self.PostAsync($"/api/v1/me/learning/courses/{privacy}/enroll", null, Ct));
        (await self.PostAsync($"/api/v1/me/learning/courses/{privacy}/enroll", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        // The daily pass lapses the certificate and, the course being mandatory, assigns it again.
        int sent = 0;
        await factory.RunAsWorkerAsync(PeopleOsApiFactory.SeededTenantId, async services => sent = await services.GetRequiredService<LearningService>().DailyAsync(Ct));
        sent.ShouldBeGreaterThanOrEqualTo(1);
        JsonElement[] after = (await admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/learning/enrollments?employeeId={joiner}&courseId={assigned.Single(row => row.GetProperty("courseCode").GetString() == "COC").GetProperty("courseId").GetGuid()}", Ct))!;
        after.ShouldContain(row => row.GetProperty("status").GetString() == "Expired");
        after.ShouldContain(row => row.GetProperty("source").GetString() == "Renewal" && row.GetProperty("status").GetString() == "Assigned");

        // A path assigned to the joiner skips what they are already taking.
        JsonElement[] courses = (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/learning/courses", Ct))!;
        JsonElement[] paths = (await OkAsync(await admin.PostAsJsonAsync("/api/v1/learning/paths", new
        {
            Code = CoreHrApi.Unique("P"),
            Title = "Security track",
            CourseIds = courses.Where(row => row.GetProperty("code").GetString() is "INFOSEC" or "DATAPRIV").Select(row => row.GetProperty("id").GetGuid()).ToArray(),
        }, Ct))).EnumerateArray().ToArray();
        Guid pathId = paths.First(row => row.GetProperty("title").GetString() == "Security track").GetProperty("id").GetGuid();
        JsonElement result = await OkAsync(await admin.PostAsJsonAsync("/api/v1/learning/enrollments/assign", new { EmployeeIds = new[] { joiner }, PathId = pathId }, Ct));
        result.GetProperty("assigned").GetInt32().ShouldBe(0);
        result.GetProperty("skipped").GetInt32().ShouldBe(2);

        (await admin.GetFromJsonAsync<JsonElement>("/api/v1/learning/dashboard", Ct)).GetProperty("assigned").GetInt32().ShouldBeGreaterThan(0);
        (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/learning/certificates", Ct))!.ShouldContain(row => row.GetProperty("employeeId").GetGuid() == joiner);
    }

    [Fact]
    public async Task A_career_path_shows_the_next_step_and_an_approved_promotion_changes_role_and_pay()
    {
        (HttpClient admin, Guid department, Guid manager, Guid report, Guid peer, HttpClient lead, HttpClient self, HttpClient colleague) = await TeamAsync();
        using (admin)
        using (lead)
        using (self)
        using (colleague)
        {
            Guid engineer = await CoreHrApi.JobAsync(admin);
            Guid senior = await CoreHrApi.JobAsync(admin);
            await OkAsync(await admin.PostAsJsonAsync($"/api/v1/employees/{report}/assignment-changes", new
            {
                ChangeType = "Redesignation",
                EffectiveFrom = Iso(Today.AddDays(-1)),
                Assignment = new { DepartmentId = department, JobId = engineer, ManagerEmployeeId = manager, EmploymentTypeCode = "FULL_TIME", WorkModeCode = "HYBRID", Designation = "Engineer" },
            }, Ct), HttpStatusCode.Created);

            await OkAsync(await admin.PostAsJsonAsync("/api/v1/career/paths", new
            {
                Name = "Engineering " + CoreHrApi.Unique("T"),
                Track = "Individual",
                Steps = new object[]
                {
                    new { Title = "Engineer", JobId = engineer, MinMonthsInRole = 12, Skills = new[] { "C#" } },
                    new { Title = "Senior engineer", JobId = senior, MinMonthsInRole = 24, Skills = new[] { "System design", "Mentoring" } },
                },
            }, Ct));
            JsonElement career = await self.GetFromJsonAsync<JsonElement>("/api/v1/me/career", Ct);
            JsonElement position = career.GetProperty("paths").EnumerateArray().Single(row => row.GetProperty("path").GetProperty("name").GetString()!.StartsWith("Engineering", StringComparison.Ordinal));
            position.GetProperty("currentStep").GetInt32().ShouldBe(0);
            position.GetProperty("next").GetProperty("title").GetString().ShouldBe("Senior engineer");
            position.GetProperty("skillsToBuild").EnumerateArray().Select(skill => skill.GetString()).ShouldBe(["System design", "Mentoring"]);

            // The manager nominates; with no approval chain it is approved and, effective today, applied.
            (await lead.PostAsJsonAsync("/api/v1/career/promotions", new { EmployeeId = report, Details = new { ProposedDesignation = "Senior Engineer", ProposedJobId = senior, ProposedAnnualCtc = 0, EffectiveDate = Iso(Today), Justification = "x" }, Submit = false }, Ct))
                .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
            JsonElement promotion = await OkAsync(await lead.PostAsJsonAsync("/api/v1/career/promotions", new
            {
                EmployeeId = report,
                Details = new { ProposedDesignation = "Senior Engineer", ProposedJobId = senior, ProposedAnnualCtc = 2_000_000m, EffectiveDate = Iso(Today), Justification = "Leads the billing platform." },
                Submit = true,
            }, Ct));
            promotion.GetProperty("status").GetString().ShouldBe("Applied");
            promotion.GetProperty("currentDesignation").GetString().ShouldBe("Engineer");

            JsonElement person = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/employees/{report}", Ct);
            person.ToString().ShouldContain("Senior Engineer");
            (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/payroll/employees/{report}/salary", Ct)).ToString().ShouldContain("2000000");
            (await self.GetFromJsonAsync<JsonElement>("/api/v1/me/career", Ct)).GetProperty("promotions").GetArrayLength().ShouldBe(1);
            (await colleague.GetAsync("/api/v1/career/promotions", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

            // A succession plan for the lead's role, with the peer ready now.
            JsonElement[] plans = (await OkAsync(await admin.PostAsJsonAsync("/api/v1/career/succession", new
            {
                RoleTitle = "Engineering lead " + CoreHrApi.Unique("R"),
                DepartmentId = department,
                IncumbentEmployeeId = report,
                Criticality = "High",
                VacancyRisk = "Medium",
                Successors = new[] { new { EmployeeId = peer, Readiness = "ReadyNow", Notes = "Runs the on-call rota" } },
            }, Ct))).EnumerateArray().ToArray();
            JsonElement plan = plans.First(row => row.GetProperty("incumbentEmployeeId").GetGuid() == report);
            plan.GetProperty("covered").GetBoolean().ShouldBeTrue();
            (await admin.PostAsJsonAsync("/api/v1/career/succession", new { RoleTitle = "Self", IncumbentEmployeeId = peer, Criticality = "Low", VacancyRisk = "Low", Successors = new[] { new { EmployeeId = peer, Readiness = "ReadyNow" } } }, Ct))
                .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        }
    }
}
