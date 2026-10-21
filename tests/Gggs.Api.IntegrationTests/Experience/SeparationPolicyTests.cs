using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Shouldly;
using static Gggs.Api.IntegrationTests.DeliveryApi;
using static Gggs.Api.IntegrationTests.PlatformServicesApi;

namespace Gggs.Api.IntegrationTests;

/// <summary>
/// Separation v2 phase A end to end: notice periods from the policy and its rules, early release
/// with or without a buyout, the manager's classification of the exit, the case timeline, and
/// leave the policy blocks while notice is served.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class SeparationPolicyTests(PeopleOsApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly (string, DataScope)[] Employee =
    [
        (Permissions.Self.ResignationSubmit, DataScope.Own),
        (Permissions.Self.LeaveView, DataScope.Own),
        (Permissions.Self.LeaveApply, DataScope.Own),
    ];

    private static object Policy(int defaultNotice = 60, bool buyout = true) => new
    {
        DefaultNoticeDays = defaultNotice,
        ProbationNoticeDays = 15,
        BuyoutAllowed = buyout,
        GardenLeaveAllowed = false,
        BlockedLeaveTypeCodes = new[] { "EL" },
        EncashableLeaveTypeCodes = new[] { "EL" },
        EncashmentCapDays = (int?)null,
        RetirementAge = 60,
        AbscondingDays = 7,
        SettlementDueWorkingDays = 2,
        GratuityDueDays = 30,
    };

    private async Task<(HttpClient Admin, Guid Person, HttpClient Self)> PersonAsync()
    {
        HttpClient admin = await factory.CreateAdminClientAsync();
        Guid person = await CoreHrApi.HireAsync(admin, await CoreHrApi.DepartmentAsync(admin));
        HttpClient self = await factory.CreateScopedUserClientAsync(Email("sep"), person, Employee);
        return (admin, person, self);
    }

    [Fact]
    public async Task Notice_comes_from_the_most_specific_rule_then_the_policy_default()
    {
        (HttpClient admin, _, HttpClient self) = await PersonAsync();
        using (admin)
        using (self)
        {
            JsonElement rule = await OkAsync(await admin.PostAsJsonAsync("/api/v1/separation/policy/notice-rules", new { EmploymentTypeCode = "full_time", NoticeDays = 90 }, Ct));
            try
            {
                rule.GetProperty("employmentTypeCode").GetString().ShouldBe("FULL_TIME");
                JsonElement resigned = await OkAsync(await self.PostAsJsonAsync("/api/v1/me/separation", new { Reason = "New role", ReasonCode = "CAREER_GROWTH" }, Ct));
                JsonElement item = resigned.GetProperty("case");
                item.GetProperty("noticePeriodDays").GetInt32().ShouldBe(90);
                item.GetProperty("reasonLabel").GetString().ShouldBe("Career growth elsewhere");
                resigned.GetProperty("events").EnumerateArray().Select(entry => entry.GetProperty("kind").GetString()).ShouldContain("submitted");
            }
            finally
            {
                await admin.DeleteAsync($"/api/v1/separation/policy/notice-rules/{rule.GetProperty("id").GetGuid()}", Ct);
            }

            (await OkAsync(await admin.GetAsync("/api/v1/separation/policy/notice-rules", Ct))).EnumerateArray()
                .ShouldNotContain(row => row.GetProperty("id").GetGuid() == rule.GetProperty("id").GetGuid());
            (await CoreHrApi.ErrorCodeAsync(await admin.PostAsJsonAsync("/api/v1/separation/policy/notice-rules", new { NoticeDays = 30 }, Ct))).ShouldBe("NOTICE_RULE_EMPTY");
        }
    }

    [Fact]
    public async Task The_policy_is_read_by_everyone_and_changed_only_by_its_managers()
    {
        (HttpClient admin, _, HttpClient self) = await PersonAsync();
        using (admin)
        using (self)
        {
            (await OkAsync(await self.GetAsync("/api/v1/separation/policy", Ct))).GetProperty("settlementDueWorkingDays").GetInt32().ShouldBe(2);
            (await self.PutAsJsonAsync("/api/v1/separation/policy", Policy(30), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await OkAsync(await self.GetAsync("/api/v1/separation/reasons", Ct))).EnumerateArray().ShouldContain(row => row.GetProperty("code").GetString() == "RELOCATION");

            try
            {
                (await OkAsync(await admin.PutAsJsonAsync("/api/v1/separation/policy", Policy(45, buyout: false), Ct))).GetProperty("defaultNoticeDays").GetInt32().ShouldBe(45);
                JsonElement resigned = await OkAsync(await self.PostAsJsonAsync("/api/v1/me/separation", new { Reason = "Personal" }, Ct));
                resigned.GetProperty("case").GetProperty("noticePeriodDays").GetInt32().ShouldBe(45);
                string release = Iso(Today.AddDays(10));
                (await CoreHrApi.ErrorCodeAsync(await self.PostAsJsonAsync("/api/v1/me/separation/early-release", new { ReleaseDay = release, WithBuyout = true }, Ct))).ShouldBe("SEPARATION_BUYOUT");
            }
            finally
            {
                await OkAsync(await admin.PutAsJsonAsync("/api/v1/separation/policy", Policy(), Ct));
            }
        }
    }

    [Fact]
    public async Task An_early_release_with_a_buyout_moves_the_last_day_and_recovers_the_shortfall()
    {
        (HttpClient admin, _, HttpClient self) = await PersonAsync();
        using (admin)
        using (self)
        {
            JsonElement resigned = await OkAsync(await self.PostAsJsonAsync("/api/v1/me/separation", new { Reason = "Joining a startup" }, Ct));
            Guid caseId = resigned.GetProperty("case").GetProperty("id").GetGuid();
            string release = Iso(Today.AddDays(20));

            JsonElement asked = await OkAsync(await self.PostAsJsonAsync("/api/v1/me/separation/early-release", new { ReleaseDay = release, WithBuyout = true }, Ct));
            asked.GetProperty("case").GetProperty("requestedReleaseDay").GetString().ShouldBe(release);

            JsonElement granted = await OkAsync(await admin.PostAsync($"/api/v1/separation/cases/{caseId}/early-release/grant", null, Ct));
            JsonElement item = granted.GetProperty("case");
            item.GetProperty("lastWorkingDay").GetString().ShouldBe(release);
            item.GetProperty("shortfallDays").GetInt32().ShouldBe(40);
            item.GetProperty("shortfallWaived").GetBoolean().ShouldBeFalse();
            granted.GetProperty("events").EnumerateArray().Select(entry => entry.GetProperty("kind").GetString()).ShouldContain("early-release-granted");
            (await CoreHrApi.ErrorCodeAsync(await admin.PostAsync($"/api/v1/separation/cases/{caseId}/early-release/grant", null, Ct))).ShouldBe("SEPARATION_RELEASE_NONE");
        }
    }

    [Fact]
    public async Task The_manager_records_why_and_whether_the_exit_is_regretted()
    {
        (HttpClient admin, _, HttpClient self) = await PersonAsync();
        using (admin)
        using (self)
        {
            Guid caseId = (await OkAsync(await self.PostAsJsonAsync("/api/v1/me/separation", new { Reason = "Better offer" }, Ct))).GetProperty("case").GetProperty("id").GetGuid();
            JsonElement classified = await OkAsync(await admin.PutAsJsonAsync($"/api/v1/separation/cases/{caseId}/classification",
                new { ReasonCode = "COMPENSATION", SecondaryReasonCode = "CAREER_GROWTH", Regretted = true, RetentionNote = "Counter-offer of 15% declined" }, Ct));
            JsonElement item = classified.GetProperty("case");
            item.GetProperty("regretted").GetBoolean().ShouldBeTrue();
            item.GetProperty("retentionNote").GetString().ShouldBe("Counter-offer of 15% declined");
            item.GetProperty("secondaryReasonCode").GetString().ShouldBe("CAREER_GROWTH");
            (await CoreHrApi.ErrorCodeAsync(await admin.PutAsJsonAsync($"/api/v1/separation/cases/{caseId}/classification", new { ReasonCode = "BORED" }, Ct))).ShouldBe("SEPARATION_REASON");
            (await self.PutAsJsonAsync($"/api/v1/separation/cases/{caseId}/classification", new { ReasonCode = "OTHER" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task Earned_leave_cannot_be_applied_for_while_serving_notice()
    {
        (HttpClient admin, Guid person, HttpClient self) = await PersonAsync();
        using (admin)
        using (self)
        {
            Guid earned = await TimeApi.LeaveTypeAsync(admin, "EL");
            await TimeApi.GrantAsync(admin, person, earned, 5);
            DateOnly tuesday = TimeApi.Next(DayOfWeek.Tuesday, daysAhead: 7);
            await OkAsync(await self.PostAsJsonAsync("/api/v1/me/separation", new { Reason = "Relocating" }, Ct));

            HttpResponseMessage refused = await self.PostAsJsonAsync("/api/v1/me/leave/requests", TimeApi.Application(earned, tuesday, tuesday), Ct);
            (await CoreHrApi.ErrorCodeAsync(refused)).ShouldBe("LEAVE_NOT_ALLOWED");

            Guid casual = await TimeApi.LeaveTypeAsync(admin, "CL");
            await TimeApi.GrantAsync(admin, person, casual, 2);
            (await self.PostAsJsonAsync("/api/v1/me/leave/requests", TimeApi.Application(casual, tuesday, tuesday), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }
}
