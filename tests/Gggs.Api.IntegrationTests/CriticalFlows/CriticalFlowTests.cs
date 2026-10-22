using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Gggs.Modules.Separation.Application;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using static Gggs.Api.IntegrationTests.DeliveryApi;
using static Gggs.Api.IntegrationTests.PlatformServicesApi;

namespace Gggs.Api.IntegrationTests;

/// <summary>
/// The critical journeys (spec §134), each crossing several modules the way a real organisation
/// runs them: hire to first payslip, leave from an assistant draft to the calendar, project staffing
/// to an invoice, and resignation to the alumni list. The per-module suites test the rules; these
/// test that the modules hand over to each other.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class CriticalFlowTests(PeopleOsApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static JsonElement[] Rows(JsonElement result)
        => result.ValueKind == JsonValueKind.Array ? [.. result.EnumerateArray()] : [.. result.GetProperty("items").EnumerateArray()];

    [Fact]
    public async Task Hire_to_first_payslip()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();

        // Core HR: the hire, with the statutory and bank details payroll needs.
        string lastName = CoreHrApi.Unique("Hire");
        JsonElement hired = await OkAsync(await admin.PostAsJsonAsync("/api/v1/employees", new
        {
            FirstName = "Journey",
            LastName = lastName,
            OfficialEmail = Email("journey"),
            JoiningDate = "2024-01-15",
            Assignment = new { DepartmentId = await CoreHrApi.DepartmentAsync(admin), EmploymentTypeCode = "FULL_TIME", WorkModeCode = "HYBRID", Designation = "Analyst" },
        }, Ct), HttpStatusCode.Created);
        Guid employee = hired.GetProperty("id").GetGuid();
        (await admin.PutAsJsonAsync($"/api/v1/employees/{employee}/statutory", new { Pan = "ABCDE1234F", Uan = "100200300499", ProfessionalTaxStateCode = "IN-KA" }, Ct)).IsSuccessStatusCode.ShouldBeTrue();
        (await admin.PutAsJsonAsync($"/api/v1/employees/{employee}/bank", new { BankName = "HDFC Bank", Ifsc = "HDFC0001234", AccountHolder = "Journey Hire", AccountNumber = "50100999888777" }, Ct)).IsSuccessStatusCode.ShouldBeTrue();
        await factory.DeliverEventsAsync();

        // Search and the HR calendar know about the new person straight away.
        (await OkAsync(await admin.GetAsync($"/api/v1/search?q={Uri.EscapeDataString(lastName)}", Ct))).GetProperty("hits").EnumerateArray()
            .ShouldContain(hit => hit.GetProperty("kind").GetString() == "employee" && hit.GetProperty("id").GetGuid() == employee);

        // Payroll: salary, the month's run, and the payslip in the person's own hands.
        Guid structure = (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/payroll/structures", Ct))!.First(row => row.GetProperty("code").GetString() == "STANDARD").GetProperty("id").GetGuid();
        await OkAsync(await admin.PostAsJsonAsync($"/api/v1/payroll/employees/{employee}/salary", new { StructureId = structure, AnnualCtc = 960_000m, EffectiveFrom = "2024-01-15", Reason = "Hire" }, Ct));

        const string period = "2025-09-01";
        Guid run = (await OkAsync(await admin.PostAsJsonAsync("/api/v1/payroll/runs", new { Period = period }, Ct))).GetProperty("id").GetGuid();
        await OkAsync(await admin.PostAsync($"/api/v1/payroll/runs/{run}/calculate", null, Ct));
        JsonElement items = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/payroll/runs/{run}/items?pageSize=500", Ct))!;
        JsonElement mine = default;
        foreach (JsonElement item in items.GetProperty("items").EnumerateArray())
        {
            if (item.GetProperty("employeeId").GetGuid() == employee)
            {
                mine = item;
            }
            else if (!item.GetProperty("onHold").GetBoolean())
            {
                await OkAsync(await admin.PostAsJsonAsync($"/api/v1/payroll/runs/{run}/items/{item.GetProperty("id").GetGuid()}/hold", new { Reason = "Not part of this journey" }, Ct));
            }
        }

        mine.ValueKind.ShouldBe(JsonValueKind.Object, "the new hire is in the run");
        await OkAsync(await admin.PostAsync($"/api/v1/payroll/runs/{run}/submit", null, Ct));
        await OkAsync(await admin.PostAsync($"/api/v1/payroll/runs/{run}/finalize", null, Ct));

        using HttpClient self = await factory.CreateScopedUserClientAsync(Email("journey-self"), employee, (Permissions.Self.PayslipView, DataScope.Own));
        JsonElement payslip = (await self.GetFromJsonAsync<JsonElement[]>("/api/v1/me/payslips", Ct))!.Single(row => row.GetProperty("period").GetString() == period);
        payslip.GetProperty("net").GetDecimal().ShouldBe(mine.GetProperty("net").GetDecimal());
        payslip.GetProperty("net").GetDecimal().ShouldBeGreaterThan(0m);
        (await admin.GetStringAsync($"/api/v1/payroll/runs/{run}/reports/bank-file", Ct)).ShouldContain("50100999888777");
    }

    [Fact]
    public async Task Leave_drafted_by_the_assistant_and_approved_by_the_manager_reaches_balance_and_calendar()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid casual = await TimeApi.LeaveTypeAsync(admin, "CL");
        await TimeApi.GrantAsync(admin, org.Requester, casual, 3);
        await using ActiveDefinition definition = await DefinitionAsync(admin, [Stage("Manager", [Rule("ReportingManager")])], subject: "leave.request");

        using HttpClient employee = await factory.CreateScopedUserClientAsync(
            Email("journey-leave"),
            org.Requester,
            (Permissions.Ai.AssistantUse, DataScope.Own),
            (Permissions.Self.LeaveView, DataScope.Own),
            (Permissions.Self.LeaveApply, DataScope.Own));
        using HttpClient manager = await TimeApi.EmployeeAsync(factory, org.Manager);

        // The assistant drafts; nothing happens until the person approves the draft.
        DateOnly tuesday = TimeApi.Next(DayOfWeek.Tuesday, daysAhead: 21);
        factory.Llm.Reset();
        factory.Llm.ReplyToolUse("apply_leave", new { leaveTypeCode = "CL", startDate = Iso(tuesday), endDate = Iso(tuesday.AddDays(1)), reason = "Family visit" });
        factory.Llm.ReplyText("I've drafted the leave for you to approve.");
        JsonElement conversation = await OkAsync(await employee.PostAsJsonAsync("/api/v1/ai/conversations", new { Assistant = "me", Message = "Book me casual leave for two days" }, Ct));
        Guid actionId = conversation.GetProperty("actions").EnumerateArray().Single().GetProperty("id").GetGuid();
        (await TimeApi.BalanceAsync(employee, "CL")).GetProperty("pending").GetDecimal().ShouldBe(0m, "a draft holds no days");

        JsonElement approved = await OkAsync(await employee.PostAsync($"/api/v1/ai/actions/{actionId}/approve", null, Ct));
        approved.GetProperty("result").GetString()!.ShouldContain("pending");
        (await TimeApi.BalanceAsync(employee, "CL")).GetProperty("pending").GetDecimal().ShouldBe(2m);

        // The manager approves through the workflow inbox.
        JsonElement request = Rows(await OkAsync(await employee.GetAsync("/api/v1/me/leave/requests", Ct)))
            .Single(row => row.GetProperty("startDate").GetString() == Iso(tuesday));
        await DecideAsync(manager, await TaskForAsync(manager, request.GetProperty("workflowInstanceId").GetGuid()), "approve");
        await factory.DeliverEventsAsync();

        JsonElement balance = await TimeApi.BalanceAsync(employee, "CL");
        balance.GetProperty("balance").GetDecimal().ShouldBe(1m);
        balance.GetProperty("pending").GetDecimal().ShouldBe(0m);

        // And it is on the HR calendar for everyone planning around it.
        JsonElement calendar = await OkAsync(await admin.GetAsync($"/api/v1/calendar?from={Iso(tuesday)}&to={Iso(tuesday.AddDays(1))}&scope=all", Ct));
        calendar.GetProperty("entries").EnumerateArray().ShouldContain(entry => entry.GetProperty("kind").GetString() == "leave" && entry.GetProperty("employeeId").GetGuid() == org.Requester);
    }

    [Fact]
    public async Task Project_staffing_to_approved_timesheet_to_invoice()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid client = (await ClientAsync(admin)).GetProperty("client").GetProperty("id").GetGuid();
        JsonElement project = await CreateProjectAsync(admin, Project(clientId: client, managerId: org.Manager, start: Today.AddDays(-60), end: Today.AddDays(60)));
        Guid projectId = project.GetProperty("id").GetGuid();

        // Staffing: the person was available, and once allocated is on the project team.
        await OkAsync(await admin.PostAsJsonAsync($"/api/v1/projects/{projectId}/team", new
        {
            EmployeeId = org.Requester,
            ProjectRoleId = await RoleAsync(admin, "DEV"),
            StartDate = Iso(Today.AddDays(-30)),
            AllocationPercent = 100,
            Billable = true,
            BillingRate = 3000m,
            CostRate = 1500m,
        }, Ct));

        // Timesheet: a billable week against the project.
        DateOnly monday = Today.AddDays(-(((int)Today.DayOfWeek + 6) % 7)).AddDays(-7);
        using HttpClient self = await factory.CreateScopedUserClientAsync(Email("journey-ts"), org.Requester, (Permissions.Self.TimesheetSubmit, DataScope.Own));
        await OkAsync(await self.PutAsJsonAsync($"/api/v1/me/timesheets/{Iso(monday)}", new
        {
            Entries = Enumerable.Range(0, 5).Select(offset => new { Date = Iso(monday.AddDays(offset)), ProjectId = projectId, Category = "Billable", Hours = 8m }).ToArray(),
        }, Ct));
        (await OkAsync(await self.PostAsync($"/api/v1/me/timesheets/{Iso(monday)}/submit", null, Ct))).GetProperty("status").GetString().ShouldBe("Approved");

        // Financials: the approved hours are revenue, then billed.
        JsonElement before = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/project-financials/{projectId}", Ct))!.GetProperty("summary");
        before.GetProperty("revenue").GetDecimal().ShouldBe(40m * 3000m);
        before.GetProperty("labourCost").GetDecimal().ShouldBe(40m * 1500m);
        before.GetProperty("unbilled").GetDecimal().ShouldBe(120_000m);

        JsonElement invoice = await OkAsync(await admin.PostAsJsonAsync($"/api/v1/project-financials/{projectId}/invoices", new
        {
            Details = new { InvoiceDate = Iso(Today), DueDate = Iso(Today.AddDays(30)), Amount = 120_000m, TaxAmount = 21_600m },
        }, Ct));
        await OkAsync(await admin.PostAsync($"/api/v1/project-financials/{projectId}/invoices/{invoice.GetProperty("id").GetGuid()}/send", null, Ct));
        JsonElement after = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/project-financials/{projectId}", Ct))!.GetProperty("summary");
        after.GetProperty("billed").GetDecimal().ShouldBe(120_000m);
        after.GetProperty("unbilled").GetDecimal().ShouldBe(0m);

        // The project is findable by the people who may see it.
        string code = project.GetProperty("code").GetString()!;
        (await OkAsync(await admin.GetAsync($"/api/v1/search?q={Uri.EscapeDataString(code)}", Ct))).GetProperty("hits").EnumerateArray()
            .ShouldContain(hit => hit.GetProperty("kind").GetString() == "project" && hit.GetProperty("id").GetGuid() == projectId);
    }

    [Fact]
    public async Task Resignation_to_settlement_exit_and_alumni()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid person = await CoreHrApi.HireAsync(admin, await CoreHrApi.DepartmentAsync(admin));
        Guid structure = (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/payroll/structures", Ct))!.First(row => row.GetProperty("code").GetString() == "STANDARD").GetProperty("id").GetGuid();
        await OkAsync(await admin.PostAsJsonAsync($"/api/v1/payroll/employees/{person}/salary", new { StructureId = structure, AnnualCtc = 840_000m, EffectiveFrom = "2024-01-15", Reason = "Hire" }, Ct));
        using HttpClient self = await factory.CreateScopedUserClientAsync(Email("journey-exit"), person, (Permissions.Self.ResignationSubmit, DataScope.Own));

        // HR records the resignation handed in a month ago; the notice period ended yesterday.
        JsonElement resigned = await OkAsync(await admin.PostAsJsonAsync("/api/v1/separation/cases", new
        {
            EmployeeId = person,
            Type = "Resignation",
            Reason = "Higher studies",
            NoticeDate = Iso(Today.AddDays(-30)),
            LastWorkingDay = Iso(Today.AddDays(-1)),
        }, Ct));
        Guid caseId = resigned.GetProperty("case").GetProperty("id").GetGuid();
        (await OkAsync(await self.GetAsync("/api/v1/me/separation", Ct))).GetProperty("case").GetProperty("id").GetGuid().ShouldBe(caseId);
        foreach (JsonElement item in resigned.GetProperty("clearance").EnumerateArray())
        {
            await OkAsync(await admin.PostAsJsonAsync($"/api/v1/separation/clearance/{item.GetProperty("id").GetGuid()}", new { Status = "Cleared" }, Ct));
        }

        // Full and final settlement: approved, then paid through its off-cycle payroll run.
        await OkAsync(await admin.PostAsync($"/api/v1/separation/cases/{caseId}/settlement/calculate", null, Ct));
        await OkAsync(await admin.PostAsync($"/api/v1/separation/cases/{caseId}/settlement/approve", null, Ct));
        (await SettlementPayroll.PayAsync(factory, admin, caseId, "NEFT-JOURNEY")).GetProperty("status").GetString().ShouldBe("Paid");

        // The nightly sweep takes the exit into effect: the person leaves the workforce and joins the alumni.
        await factory.RunAsWorkerAsync(PeopleOsApiFactory.SeededTenantId, async services => await services.GetRequiredService<SeparationService>().SweepAsync(Ct));
        (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/employees/{person}", Ct)).GetProperty("status").GetString().ShouldBe("Exited");
        (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/separation/alumni", Ct))!.ShouldContain(row => row.GetProperty("employeeId").GetGuid() == person);
        JsonElement closed = await OkAsync(await admin.GetAsync($"/api/v1/separation/cases/{caseId}", Ct));
        closed.GetProperty("settlement").GetProperty("status").GetString().ShouldBe("Paid");

        // Leaving switches the sign-in off: the former employee's session no longer works.
        closed.GetProperty("events").EnumerateArray().Select(row => row.GetProperty("kind").GetString()).ShouldContain("access-disabled");
        (await self.GetAsync("/api/v1/me/separation", Ct)).StatusCode.ShouldBeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }
}
