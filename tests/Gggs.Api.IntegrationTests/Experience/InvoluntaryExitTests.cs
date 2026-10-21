using System.Net.Http.Json;
using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Separation.Application;
using Gggs.Modules.Separation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using static Gggs.Api.IntegrationTests.DeliveryApi;
using static Gggs.Api.IntegrationTests.PlatformServicesApi;

namespace Gggs.Api.IntegrationTests;

/// <summary>
/// Separation v2 phase C end to end: termination after a show-cause, retrenchment with
/// compensation and notice pay, absconding found from attendance and handled through notices, a
/// death with the nominee and gratuity without minimum service, and retirements and contract ends
/// proposed for HR to confirm.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class InvoluntaryExitTests(PeopleOsApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly (string, DataScope)[] Employee = [(Permissions.Self.ResignationSubmit, DataScope.Own)];

    private static JsonElement Line(JsonElement view, string code)
        => view.GetProperty("settlement").GetProperty("lines").EnumerateArray().First(line => line.GetProperty("code").GetString() == code);

    private async Task SalaryAsync(HttpClient admin, Guid person)
    {
        Guid structure = (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/payroll/structures", Ct))!.First(row => row.GetProperty("code").GetString() == "STANDARD").GetProperty("id").GetGuid();
        await OkAsync(await admin.PostAsJsonAsync($"/api/v1/payroll/employees/{person}/salary", new { StructureId = structure, AnnualCtc = 840_000m, EffectiveFrom = "2024-01-15", Reason = "Hire" }, Ct));
    }

    [Fact]
    public async Task A_show_cause_is_answered_then_ends_in_termination_with_notice_pay()
    {
        HttpClient admin = await factory.CreateAdminClientAsync();
        Guid person = await CoreHrApi.HireAsync(admin, await CoreHrApi.DepartmentAsync(admin));
        await SalaryAsync(admin, person);
        HttpClient self = await factory.CreateScopedUserClientAsync(Email("showcause"), person, Employee);
        using (admin)
        using (self)
        {
            JsonElement issued = await OkAsync(await admin.PostAsJsonAsync("/api/v1/separation/show-causes", new { EmployeeId = person, Allegation = "Shared client source code on a personal drive." }, Ct));
            Guid id = issued.GetProperty("id").GetGuid();
            issued.GetProperty("status").GetString().ShouldBe("Issued");
            issued.GetProperty("replyDueOn").GetString().ShouldBe(Iso(Today.AddDays(7)));
            issued.TryGetProperty("noticeLetterId", out _).ShouldBeTrue("the notice is generated as a letter");
            (await CoreHrApi.ErrorCodeAsync(await admin.PostAsJsonAsync($"/api/v1/separation/show-causes/{id}/decision", new { Outcome = "Termination" }, Ct))).ShouldBe("SHOW_CAUSE_NOT_HEARD");

            JsonElement mine = await OkAsync(await self.GetAsync("/api/v1/me/separation/show-causes", Ct));
            mine.EnumerateArray().ShouldContain(row => row.GetProperty("id").GetGuid() == id);
            JsonElement replied = await OkAsync(await self.PostAsJsonAsync($"/api/v1/me/separation/show-causes/{id}/reply", new { Reply = "It was a backup; I have deleted it." }, Ct));
            replied.GetProperty("status").GetString().ShouldBe("Replied");

            JsonElement decided = await OkAsync(await admin.PostAsJsonAsync($"/api/v1/separation/show-causes/{id}/decision",
                new { Outcome = "Termination", Note = "Breach of the confidentiality policy", LastWorkingDay = Iso(Today), PayInLieuDays = 30 }, Ct));
            decided.GetProperty("outcome").GetString().ShouldBe("Termination");
            Guid caseId = decided.GetProperty("separationCaseId").GetGuid();

            JsonElement separation = await OkAsync(await admin.GetAsync($"/api/v1/separation/cases/{caseId}", Ct));
            separation.GetProperty("case").GetProperty("type").GetString().ShouldBe("Termination");
            separation.GetProperty("case").GetProperty("payInLieuDays").GetInt32().ShouldBe(30);
            separation.GetProperty("case").GetProperty("reasonCode").GetString().ShouldBe("MISCONDUCT");
            separation.GetProperty("case").GetProperty("eligibleForRehire").GetBoolean().ShouldBeFalse();

            JsonElement settled = await OkAsync(await admin.PostAsync($"/api/v1/separation/cases/{caseId}/settlement/calculate", null, Ct));
            Line(settled, "NOTICE_PAY").GetProperty("basis").GetString().ShouldBe("30 days x gross/30");
            settled.GetProperty("settlement").GetProperty("lines").EnumerateArray().ShouldNotContain(line => line.GetProperty("code").GetString() == "NOTICE_RECOVERY");
        }
    }

    [Fact]
    public async Task A_warning_closes_the_show_cause_without_an_exit()
    {
        HttpClient admin = await factory.CreateAdminClientAsync();
        Guid person = await CoreHrApi.HireAsync(admin, await CoreHrApi.DepartmentAsync(admin));
        Guid officer = await CoreHrApi.HireAsync(admin, await CoreHrApi.DepartmentAsync(admin));
        using (admin)
        {
            Guid id = (await OkAsync(await admin.PostAsJsonAsync("/api/v1/separation/show-causes", new { EmployeeId = person, Allegation = "Late to work eleven times this month.", ReplyDays = 3 }, Ct))).GetProperty("id").GetGuid();
            (await OkAsync(await admin.PostAsJsonAsync($"/api/v1/separation/show-causes/{id}/enquiry", new { OfficerEmployeeId = officer }, Ct))).GetProperty("status").GetString().ShouldBe("UnderEnquiry");
            await OkAsync(await admin.PostAsJsonAsync($"/api/v1/separation/show-causes/{id}/findings", new { Findings = "Lateness confirmed; caring for a parent." }, Ct));
            JsonElement warned = await OkAsync(await admin.PostAsJsonAsync($"/api/v1/separation/show-causes/{id}/decision", new { Outcome = "Warning", Note = "Flexible hours agreed" }, Ct));
            warned.GetProperty("status").GetString().ShouldBe("Closed");
            warned.TryGetProperty("separationCaseId", out _).ShouldBeFalse();
            (await OkAsync(await admin.GetAsync("/api/v1/separation/show-causes?open=true", Ct))).EnumerateArray().ShouldNotContain(row => row.GetProperty("id").GetGuid() == id);
        }
    }

    [Fact]
    public async Task Retrenchment_opens_a_case_per_person_with_compensation_and_pay_for_the_missing_notice()
    {
        HttpClient admin = await factory.CreateAdminClientAsync();
        Guid department = await CoreHrApi.DepartmentAsync(admin);
        Guid first = await CoreHrApi.HireAsync(admin, department);
        Guid second = await CoreHrApi.HireAsync(admin, department);
        await SalaryAsync(admin, first);
        using (admin)
        {
            (await CoreHrApi.ErrorCodeAsync(await admin.PostAsJsonAsync("/api/v1/separation/retrenchment", new
            {
                EmployeeIds = new[] { first },
                Reason = "Closure of the support unit",
                NoticeDate = Iso(Today),
                LastWorkingDay = Iso(Today.AddDays(10)),
                PayInLieuOfNotice = false,
            }, Ct))).ShouldBe("RETRENCHMENT_NOTICE");

            await OkAsync(await admin.PostAsJsonAsync("/api/v1/separation/cases", new { EmployeeId = second, Type = "Mutual", Reason = "Agreed", NoticeDate = Iso(Today), LastWorkingDay = Iso(Today.AddDays(5)) }, Ct));
            JsonElement results = await OkAsync(await admin.PostAsJsonAsync("/api/v1/separation/retrenchment", new
            {
                EmployeeIds = new[] { first, second },
                Reason = "Closure of the support unit",
                NoticeDate = Iso(Today),
                LastWorkingDay = Iso(Today.AddDays(10)),
                PayInLieuOfNotice = true,
            }, Ct));
            JsonElement done = results.EnumerateArray().Single(row => row.GetProperty("employeeId").GetGuid() == first);
            done.GetProperty("payInLieuDays").GetInt32().ShouldBe(20);
            results.EnumerateArray().Single(row => row.GetProperty("employeeId").GetGuid() == second).GetProperty("error").GetString()!.ShouldContain("already in progress");

            Guid caseId = done.GetProperty("caseId").GetGuid();
            JsonElement settled = await OkAsync(await admin.PostAsync($"/api/v1/separation/cases/{caseId}/settlement/calculate", null, Ct));
            Line(settled, "RETRENCHMENT").GetProperty("basis").GetString()!.ShouldStartWith("3 year(s)");
            Line(settled, "NOTICE_PAY").GetProperty("basis").GetString().ShouldBe("20 days x gross/30");
            settled.GetProperty("case").GetProperty("type").GetString().ShouldBe("Retrenchment");
        }
    }

    [Fact]
    public async Task A_long_unexplained_absence_is_flagged_noticed_and_treated_as_absconding()
    {
        HttpClient admin = await factory.CreateAdminClientAsync();
        Guid person = await CoreHrApi.HireAsync(admin, await CoreHrApi.DepartmentAsync(admin));
        DateOnly worked = TimeApi.Previous(DayOfWeek.Monday, daysBack: 16);
        await TimeApi.PunchAtAsync(factory, person, (worked, new TimeOnly(9, 0)), (worked, new TimeOnly(18, 0)));
        using (admin)
        {
            await OkAsync(await admin.PostAsync("/api/v1/separation/absences/detect", null, Ct));
            JsonElement alert = (await OkAsync(await admin.GetAsync("/api/v1/separation/absences?open=true", Ct))).EnumerateArray().Single(row => row.GetProperty("employeeId").GetGuid() == person);
            alert.GetProperty("lastWorkedDay").GetString().ShouldBe(Iso(worked));
            alert.GetProperty("workingDaysAbsent").GetInt32().ShouldBeGreaterThanOrEqualTo(7);
            alert.GetProperty("canSeparate").GetBoolean().ShouldBeFalse();
            Guid id = alert.GetProperty("id").GetGuid();

            await OkAsync(await admin.PostAsync("/api/v1/separation/absences/detect", null, Ct));
            (await OkAsync(await admin.GetAsync("/api/v1/separation/absences?open=true", Ct))).EnumerateArray().Count(row => row.GetProperty("employeeId").GetGuid() == person).ShouldBe(1, "a run raises one alert");

            JsonElement noticed = await OkAsync(await admin.PostAsync($"/api/v1/separation/absences/{id}/notice", null, Ct));
            noticed.GetProperty("noticesSent").GetInt32().ShouldBe(1);
            noticed.TryGetProperty("lastLetterId", out _).ShouldBeTrue();
            (await CoreHrApi.ErrorCodeAsync(await admin.PostAsync($"/api/v1/separation/absences/{id}/separate", null, Ct))).ShouldBe("ABSENCE_NOT_LAPSED");

            // A week later, still no word.
            await factory.RunAsWorkerAsync(PeopleOsApiFactory.SeededTenantId, async services =>
            {
                AppDbContext database = services.GetRequiredService<AppDbContext>();
                AbsenceAlert row = await database.Set<AbsenceAlert>().SingleAsync(entry => entry.Id == id, Ct);
                database.Entry(row).Property(entry => entry.RespondBy).CurrentValue = Today.AddDays(-1);
                await database.SaveChangesAsync(Ct);
            });
            JsonElement separated = await OkAsync(await admin.PostAsync($"/api/v1/separation/absences/{id}/separate", null, Ct));
            separated.GetProperty("status").GetString().ShouldBe("Separated");
            Guid caseId = separated.GetProperty("separationCaseId").GetGuid();
            JsonElement view = await OkAsync(await admin.GetAsync($"/api/v1/separation/cases/{caseId}", Ct));
            view.GetProperty("case").GetProperty("type").GetString().ShouldBe("Absconding");
            view.GetProperty("case").GetProperty("lastWorkingDay").GetString().ShouldBe(Iso(worked));

            await factory.RunAsWorkerAsync(PeopleOsApiFactory.SeededTenantId, async services => await services.GetRequiredService<SeparationService>().SweepAsync(Ct));
            (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/employees/{person}", Ct)).GetProperty("status").GetString().ShouldBe("Exited");
        }
    }

    [Fact]
    public async Task A_death_names_the_nominee_and_pays_gratuity_without_the_five_year_minimum()
    {
        HttpClient admin = await factory.CreateAdminClientAsync();
        Guid person = await CoreHrApi.HireAsync(admin, await CoreHrApi.DepartmentAsync(admin));
        await SalaryAsync(admin, person);
        using (admin)
        {
            (await CoreHrApi.ErrorCodeAsync(await admin.PostAsJsonAsync("/api/v1/separation/cases", new
            {
                EmployeeId = person, Type = "Resignation", Reason = "x", NoticeDate = Iso(Today), LastWorkingDay = Iso(Today), NomineeName = "Asha",
            }, Ct))).ShouldBe("SEPARATION_NOMINEE");

            JsonElement opened = await OkAsync(await admin.PostAsJsonAsync("/api/v1/separation/cases", new
            {
                EmployeeId = person,
                Type = "Death",
                Reason = "Passed away after an illness",
                NoticeDate = Iso(Today),
                LastWorkingDay = Iso(Today),
                NomineeName = "Asha Rao",
                NomineeRelation = "Spouse",
                NomineeContact = "asha.rao@example.com",
            }, Ct));
            opened.GetProperty("case").GetProperty("nomineeName").GetString().ShouldBe("Asha Rao");
            foreach (JsonElement task in opened.GetProperty("offboarding").GetProperty("tasks").EnumerateArray().Where(task => task.GetProperty("owner").GetString() == "Employee"))
            {
                task.TryGetProperty("assigneeEmployeeId", out _).ShouldBeFalse("nobody is asked to hand over for someone who has died");
            }

            Guid caseId = opened.GetProperty("case").GetProperty("id").GetGuid();
            JsonElement settled = await OkAsync(await admin.PostAsync($"/api/v1/separation/cases/{caseId}/settlement/calculate", null, Ct));
            JsonElement gratuity = Line(settled, "GRATUITY");
            gratuity.GetProperty("label").GetString().ShouldBe("Gratuity (to Asha Rao)");
            gratuity.GetProperty("amount").GetDecimal().ShouldBeGreaterThan(0m);
        }
    }

    [Fact]
    public async Task Retirements_and_contract_ends_are_proposed_and_wait_for_hr()
    {
        HttpClient admin = await factory.CreateAdminClientAsync();
        Guid department = await CoreHrApi.DepartmentAsync(admin);
        Guid retiring = await CoreHrApi.HireAsync(admin, department);
        Guid contractor = await CoreHrApi.HireAsync(admin, department);
        using (admin)
        {
            DateOnly born = Today.AddYears(-60).AddDays(20);
            (await admin.PutAsJsonAsync($"/api/v1/employees/{retiring}/personal", new { FirstName = "Retiring", DateOfBirth = Iso(born) }, Ct)).EnsureSuccessStatusCode();
            (await admin.PutAsJsonAsync($"/api/v1/employees/{contractor}/contract-end", new { ContractEndDate = Iso(Today.AddDays(30)) }, Ct)).EnsureSuccessStatusCode();
            (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/employees/{contractor}", Ct)).GetProperty("contractEndDate").GetString().ShouldBe(Iso(Today.AddDays(30)));

            (await OkAsync(await admin.PostAsync("/api/v1/separation/exit-proposals/run", null, Ct))).GetProperty("proposed").GetInt32().ShouldBeGreaterThanOrEqualTo(2);
            JsonElement[] pending = [.. (await OkAsync(await admin.GetAsync("/api/v1/separation/cases?status=PendingApproval", Ct))).EnumerateArray()];
            JsonElement retirement = pending.Single(row => row.GetProperty("employeeId").GetGuid() == retiring);
            retirement.GetProperty("type").GetString().ShouldBe("Retirement");
            retirement.GetProperty("awaitingConfirmation").GetBoolean().ShouldBeTrue();
            DateOnly due = born.AddYears(60);
            retirement.GetProperty("lastWorkingDay").GetString().ShouldBe(Iso(new DateOnly(due.Year, due.Month, DateTime.DaysInMonth(due.Year, due.Month))));
            JsonElement contract = pending.Single(row => row.GetProperty("employeeId").GetGuid() == contractor);
            contract.GetProperty("type").GetString().ShouldBe("ContractEnd");

            (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/employees/{retiring}", Ct)).GetProperty("status").GetString().ShouldBe("Active", "nothing happens until HR confirms");
            JsonElement confirmed = await OkAsync(await admin.PostAsync($"/api/v1/separation/cases/{retirement.GetProperty("id").GetGuid()}/confirm", null, Ct));
            confirmed.GetProperty("case").GetProperty("status").GetString().ShouldBe("Approved");
            confirmed.GetProperty("offboarding").GetProperty("tasks").GetArrayLength().ShouldBeGreaterThan(0);

            Guid declined = contract.GetProperty("id").GetGuid();
            await OkAsync(await admin.PostAsync($"/api/v1/separation/cases/{declined}/withdraw", null, Ct));
            await OkAsync(await admin.PostAsync("/api/v1/separation/exit-proposals/run", null, Ct));
            (await OkAsync(await admin.GetAsync("/api/v1/separation/cases?status=PendingApproval", Ct))).EnumerateArray()
                .ShouldNotContain(row => row.GetProperty("employeeId").GetGuid() == contractor, "a declined proposal is not raised again for the same date");
            (await CoreHrApi.ErrorCodeAsync(await admin.PostAsync($"/api/v1/separation/cases/{declined}/confirm", null, Ct))).ShouldBe("SEPARATION_CLOSED");
        }
    }
}
