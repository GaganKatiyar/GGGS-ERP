using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Project.Application;
using Gggs.Modules.Project.Domain;
using Gggs.Modules.Project.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using static Gggs.Api.IntegrationTests.DeliveryApi;
using static Gggs.Api.IntegrationTests.PlatformServicesApi;

namespace Gggs.Api.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class ServiceLevelTests(PeopleOsApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateOnly ThisMonth => new(Today.Year, Today.Month, 1);

    private static DateOnly LastMonth => ThisMonth.AddMonths(-1);

    private static object Body(Guid manager, DateOnly? start = null, Guid? clientId = null) => new
    {
        Code = "S" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
        Name = "Support " + CoreHrApi.Unique("N"),
        Type = "Implementation",
        ClientId = clientId,
        ProjectManagerEmployeeId = manager,
        StartDate = Iso(start ?? Today.AddDays(-90)),
        EndDate = Iso(Today.AddDays(90)),
        Priority = "High",
        BillingType = "ManagedService",
        Currency = "INR",
        Confidentiality = "Internal",
        Methodology = "Agile",
    };

    private static object Policy(IEnumerable<Guid>? paused = null, decimal cap = 15, decimal credit = 5) => new
    {
        Calendar = new { AlwaysOn = true, DayStart = "00:00", DayEnd = "00:00", WorkingDays = Array.Empty<string>() },
        Targets = new[]
        {
            new { Priority = "Critical", ResponseMinutes = 30, ResolutionMinutes = 240, CreditPercent = credit },
            new { Priority = "Low", ResponseMinutes = 480, ResolutionMinutes = 2880, CreditPercent = 1m },
        },
        PausedColumnIds = paused ?? [],
        MonthlyCreditCapPercent = cap,
    };

    private static async Task<Guid> SupportProjectAsync(HttpClient admin, Guid manager, DateOnly? start = null, IEnumerable<Guid>? paused = null, decimal credit = 5)
    {
        Guid project = (await CreateProjectAsync(admin, Body(manager, start))).GetProperty("id").GetGuid();
        await OkAsync(await admin.PutAsJsonAsync($"/api/v1/projects/{project}/service-levels/policy", Policy(paused, credit: credit), Ct));
        return project;
    }

    private static async Task<Guid> TicketAsync(HttpClient admin, Guid project, string type = "Incident", string priority = "Critical")
        => (await OkAsync(await admin.PostAsJsonAsync($"/api/v1/projects/{project}/tasks", new { Type = type, Title = "Payments are down", Priority = priority, Billable = false, Progress = 0 }, Ct))).GetProperty("id").GetGuid();

    private static async Task<JsonElement> ViewAsync(HttpClient client, Guid project, DateOnly? month = null)
        => await client.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{project}/service-levels?month={Iso(month ?? ThisMonth)}", Ct);

    private static JsonElement TicketIn(JsonElement view, Guid task)
        => view.GetProperty("tickets").EnumerateArray().Single(row => row.GetProperty("taskId").GetGuid() == task);

    private static async Task<(Guid Todo, Guid Doing, Guid Done)> ColumnsAsync(HttpClient admin, Guid project)
    {
        JsonElement[] columns = [.. (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{project}/board", Ct)).GetProperty("columns").EnumerateArray()];
        Guid Of(string category) => columns.First(column => column.GetProperty("category").GetString() == category).GetProperty("id").GetGuid();
        return (Of("Todo"), Of("InProgress"), Of("Done"));
    }

    private static async Task MoveAsync(HttpClient admin, Guid project, Guid task, Guid column)
        => await OkAsync(await admin.PostAsJsonAsync($"/api/v1/projects/{project}/tasks/{task}/move", new { ColumnId = column }, Ct));

    /// <summary>Puts a ticket's targets in the past, as if it had been opened then, and runs the half-hourly sweep.</summary>
    private async Task<int> MissTargetsAsync(Guid task, DateTime opened)
    {
        int swept = 0;
        await factory.RunAsWorkerAsync(PeopleOsApiFactory.SeededTenantId, async services =>
        {
            AppDbContext db = services.GetRequiredService<AppDbContext>();
            await db.TicketSlas.Where(row => row.TaskId == task).ExecuteUpdateAsync(
                row => row.SetProperty(ticket => ticket.OpenedAt, opened)
                    .SetProperty(ticket => ticket.ResponseDueAt, opened.AddMinutes(30))
                    .SetProperty(ticket => ticket.ResolutionDueAt, opened.AddHours(4)),
                Ct);
            swept = await services.GetRequiredService<ServiceLevelService>().SweepAsync(DateTime.UtcNow, Ct);
        });
        await factory.DeliverEventsAsync();
        return swept;
    }

    // ----- Tickets and their clocks ---------------------------------------------------------------

    [Theory]
    [InlineData("Incident")]
    [InlineData("ServiceRequest")]
    public async Task ATicket_StartsItsClockWhenOpened(string type)
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid project = await SupportProjectAsync(admin, org.Manager);

        Guid task = await TicketAsync(admin, project, type);

        JsonElement ticket = TicketIn(await ViewAsync(admin, project), task);
        ticket.GetProperty("type").GetString().ShouldBe(type);
        DateTime opened = ticket.GetProperty("openedAt").GetDateTime();
        ticket.GetProperty("responseDueAt").GetDateTime().ShouldBe(opened.AddMinutes(30));
        ticket.GetProperty("resolutionDueAt").GetDateTime().ShouldBe(opened.AddHours(4));
    }

    [Fact]
    public async Task OrdinaryTasks_HaveNoClock_AndNeitherDoTicketsWithNoPolicy()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid project = await SupportProjectAsync(admin, org.Manager);
        await TicketAsync(admin, project, "Task");

        (await ViewAsync(admin, project)).GetProperty("tickets").GetArrayLength().ShouldBe(0);

        Guid bare = (await CreateProjectAsync(admin, Body(org.Manager))).GetProperty("id").GetGuid();
        await TicketAsync(admin, bare);
        JsonElement view = await ViewAsync(admin, bare);
        view.GetProperty("tickets").GetArrayLength().ShouldBe(0);
        view.TryGetProperty("policy", out _).ShouldBeFalse();
        view.GetProperty("defaults").GetProperty("targets").GetArrayLength().ShouldBe(4);
    }

    [Fact]
    public async Task ThePriorityOfATicket_NotCoveredByThePolicy_HasNoClock()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid project = await SupportProjectAsync(admin, org.Manager);

        await TicketAsync(admin, project, priority: "Medium");

        (await ViewAsync(admin, project)).GetProperty("tickets").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task MovingATicket_DrivesItsClock()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid project = await SupportProjectAsync(admin, org.Manager);
        (Guid _, Guid doing, Guid done) = await ColumnsAsync(admin, project);
        Guid task = await TicketAsync(admin, project);

        TicketIn(await ViewAsync(admin, project), task).TryGetProperty("respondedAt", out _).ShouldBeFalse();

        await MoveAsync(admin, project, task, doing);
        TicketIn(await ViewAsync(admin, project), task).TryGetProperty("respondedAt", out _).ShouldBeTrue("picking it up is the first response");

        await MoveAsync(admin, project, task, done);
        TicketIn(await ViewAsync(admin, project), task).TryGetProperty("resolvedAt", out _).ShouldBeTrue();

        await MoveAsync(admin, project, task, doing);
        JsonElement reopened = TicketIn(await ViewAsync(admin, project), task);
        reopened.TryGetProperty("resolvedAt", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task AWaitingColumn_PausesTheClock_AndTheSweepLeavesItAlone()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid project = (await CreateProjectAsync(admin, Body(org.Manager))).GetProperty("id").GetGuid();
        (Guid _, Guid doing, Guid _) = await ColumnsAsync(admin, project);
        await OkAsync(await admin.PutAsJsonAsync($"/api/v1/projects/{project}/service-levels/policy", Policy([doing]), Ct));
        Guid task = await TicketAsync(admin, project);

        await MoveAsync(admin, project, task, doing);
        TicketIn(await ViewAsync(admin, project), task).GetProperty("isPaused").GetBoolean().ShouldBeTrue();

        await MissTargetsAsync(task, DateTime.UtcNow.AddHours(-6));

        TicketIn(await ViewAsync(admin, project), task).GetProperty("resolutionBreached").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task ABoardColumnFromAnotherProject_CannotPauseThisOne()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid project = (await CreateProjectAsync(admin, Body(org.Manager))).GetProperty("id").GetGuid();

        HttpResponseMessage bad = await admin.PutAsJsonAsync($"/api/v1/projects/{project}/service-levels/policy", Policy([Guid.NewGuid()]), Ct);

        (await CoreHrApi.ErrorCodeAsync(bad)).ShouldBe("SLA_COLUMN");
    }

    [Fact]
    public async Task ChangingPriority_ChangesTheTargets()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid project = await SupportProjectAsync(admin, org.Manager);
        Guid task = await TicketAsync(admin, project, priority: "Low");

        await OkAsync(await admin.PutAsJsonAsync($"/api/v1/projects/{project}/tasks/{task}", new { Type = "Incident", Title = "Payments are down", Priority = "Critical", Billable = false, Progress = 0 }, Ct));

        JsonElement ticket = TicketIn(await ViewAsync(admin, project), task);
        ticket.GetProperty("priority").GetString().ShouldBe("Critical");
        ticket.GetProperty("resolutionDueAt").GetDateTime().ShouldBe(ticket.GetProperty("openedAt").GetDateTime().AddHours(4));
    }

    // ----- Breaches -------------------------------------------------------------------------------

    [Fact]
    public async Task TheSweep_RecordsABreach_AndTellsTheManager()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid project = await SupportProjectAsync(admin, org.Manager);
        Guid task = await TicketAsync(admin, project);

        (await MissTargetsAsync(task, DateTime.UtcNow.AddHours(-1))).ShouldBeGreaterThanOrEqualTo(1);

        JsonElement ticket = TicketIn(await ViewAsync(admin, project), task);
        ticket.GetProperty("responseBreached").GetBoolean().ShouldBeTrue();
        ticket.GetProperty("resolutionBreached").GetBoolean().ShouldBeFalse();

        using HttpClient manager = await PersonAsync(factory, org.Manager);
        JsonElement inbox = await manager.GetFromJsonAsync<JsonElement>("/api/v1/notifications", Ct);
        inbox.ToString().ShouldContain("response target");
    }

    [Fact]
    public async Task ABreachOnRecord_StopsTheTicketBeingDeletedOrRetyped_UntilExcluded()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid project = await SupportProjectAsync(admin, org.Manager);
        Guid task = await TicketAsync(admin, project);
        await MissTargetsAsync(task, DateTime.UtcNow.AddHours(-1));

        (await CoreHrApi.ErrorCodeAsync(await admin.DeleteAsync($"/api/v1/projects/{project}/tasks/{task}", Ct))).ShouldBe("SLA_BREACH_ON_RECORD");
        HttpResponseMessage retyped = await admin.PutAsJsonAsync($"/api/v1/projects/{project}/tasks/{task}", new { Type = "Task", Title = "Payments are down", Priority = "Critical", Billable = false, Progress = 0 }, Ct);
        (await CoreHrApi.ErrorCodeAsync(retyped)).ShouldBe("SLA_BREACH_ON_RECORD");

        await OkAsync(await admin.PostAsJsonAsync($"/api/v1/projects/{project}/tickets/{task}/exclude", new { Reason = "Client's own outage" }, Ct));
        (await admin.DeleteAsync($"/api/v1/projects/{project}/tasks/{task}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task APolicy_CannotBeRemovedWhileTicketsRunOnIt()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid project = await SupportProjectAsync(admin, org.Manager);
        (Guid _, Guid _, Guid done) = await ColumnsAsync(admin, project);
        Guid task = await TicketAsync(admin, project);

        (await CoreHrApi.ErrorCodeAsync(await admin.DeleteAsync($"/api/v1/projects/{project}/service-levels/policy", Ct))).ShouldBe("SLA_POLICY_IN_USE");

        await MoveAsync(admin, project, task, done);
        (await admin.DeleteAsync($"/api/v1/projects/{project}/service-levels/policy", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    // ----- Credits reach the invoice --------------------------------------------------------------

    private static async Task<JsonElement> PreviewLastMonthAsync(HttpClient admin, Guid project)
        => await admin.GetFromJsonAsync<JsonElement>($"/api/v1/project-financials/{project}/billing/preview?from={Iso(LastMonth)}&to={Iso(ThisMonth.AddDays(-1))}", Ct);

    private static async Task<Guid> BilledSupportAsync(HttpClient admin, Guid manager)
    {
        Guid project = await SupportProjectAsync(admin, manager, LastMonth);
        await OkAsync(await admin.PutAsJsonAsync($"/api/v1/project-financials/{project}/profile", new { RetainerMonthlyFee = 100000m }, Ct));
        return project;
    }

    private static DateTime MiddleOfLastMonth => LastMonth.AddDays(14).ToDateTime(new TimeOnly(6, 0), DateTimeKind.Utc);

    [Fact]
    public async Task ABreachLastMonth_ComesOffTheInvoice_AndOutOfRevenue()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid project = await BilledSupportAsync(admin, org.Manager);
        Guid task = await TicketAsync(admin, project);
        await MissTargetsAsync(task, MiddleOfLastMonth);

        JsonElement preview = await PreviewLastMonthAsync(admin, project);
        preview.GetProperty("amount").GetDecimal().ShouldBe(95000m);

        JsonElement finance = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/project-financials/{project}", Ct);
        JsonElement last = finance.GetProperty("summary").GetProperty("retainerMonths").EnumerateArray().Single(row => row.GetProperty("month").GetString() == Iso(LastMonth));
        last.GetProperty("credit").GetDecimal().ShouldBe(5000m);
        last.GetProperty("revenue").GetDecimal().ShouldBe(95000m);

        JsonElement invoice = await OkAsync(await admin.PostAsJsonAsync($"/api/v1/project-financials/{project}/invoices/prepare", new { From = Iso(LastMonth), To = Iso(ThisMonth.AddDays(-1)) }, Ct));
        invoice.GetProperty("amount").GetDecimal().ShouldBe(95000m);
        JsonElement credit = invoice.GetProperty("lines").EnumerateArray().Single(line => line.GetProperty("kind").GetString() == "ServiceCredit");
        credit.GetProperty("amount").GetDecimal().ShouldBe(-5000m);

        (await PreviewLastMonthAsync(admin, project)).GetProperty("lines").GetArrayLength().ShouldBe(0, "not given twice");
    }

    [Fact]
    public async Task AnExcludedBreach_CostsNothing_AndReinstatingBringsItBack()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid project = await BilledSupportAsync(admin, org.Manager);
        Guid task = await TicketAsync(admin, project);
        await MissTargetsAsync(task, MiddleOfLastMonth);

        JsonElement excluded = await OkAsync(await admin.PostAsJsonAsync($"/api/v1/projects/{project}/tickets/{task}/exclude", new { Reason = "Client's own outage" }, Ct));
        TicketIn(excluded, task).GetProperty("excludedReason").GetString().ShouldBe("Client's own outage");
        (await PreviewLastMonthAsync(admin, project)).GetProperty("amount").GetDecimal().ShouldBe(100000m);

        JsonElement report = (await ViewAsync(admin, project, LastMonth)).GetProperty("report");
        report.GetProperty("breaches").GetInt32().ShouldBe(0);
        report.GetProperty("excluded").GetInt32().ShouldBe(1);

        await OkAsync(await admin.PostAsync($"/api/v1/projects/{project}/tickets/{task}/reinstate", null, Ct));
        (await PreviewLastMonthAsync(admin, project)).GetProperty("amount").GetDecimal().ShouldBe(95000m);
    }

    [Fact]
    public async Task ACreditTooBigForTheInvoice_Waits()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid project = await BilledSupportAsync(admin, org.Manager);
        await OkAsync(await admin.PostAsJsonAsync($"/api/v1/project-financials/{project}/invoices/prepare", new { From = Iso(LastMonth), To = Iso(ThisMonth.AddDays(-1)) }, Ct));
        Guid task = await TicketAsync(admin, project);
        await MissTargetsAsync(task, MiddleOfLastMonth);

        JsonElement preview = await PreviewLastMonthAsync(admin, project);

        preview.GetProperty("lines").GetArrayLength().ShouldBe(0);
        preview.GetProperty("creditsWaiting").GetDecimal().ShouldBe(5000m);
    }

    // ----- The report -----------------------------------------------------------------------------

    [Fact]
    public async Task TheMonthlyReport_GoesByClient_AndExports()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid client = (await ClientAsync(admin)).GetProperty("client").GetProperty("id").GetGuid();
        Guid project = (await CreateProjectAsync(admin, Body(org.Manager, clientId: client))).GetProperty("id").GetGuid();
        await OkAsync(await admin.PutAsJsonAsync($"/api/v1/projects/{project}/service-levels/policy", Policy(), Ct));
        Guid other = await SupportProjectAsync(admin, org.Manager);
        Guid task = await TicketAsync(admin, project);
        await MissTargetsAsync(task, MiddleOfLastMonth);

        JsonElement[] rows = (await admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/projects/service-levels?month={Iso(LastMonth)}&clientId={client}", Ct))!;

        JsonElement row = rows.Single();
        row.GetProperty("projectId").GetGuid().ShouldBe(project);
        row.GetProperty("report").GetProperty("creditPercent").GetDecimal().ShouldBe(5m);
        row.GetProperty("clientName").GetString().ShouldNotBeNullOrEmpty();
        other.ShouldNotBe(project);

        HttpResponseMessage file = await admin.GetAsync($"/api/v1/projects/service-levels/export?month={Iso(LastMonth)}&clientId={client}", Ct);
        file.Content.Headers.ContentType!.MediaType.ShouldBe("text/csv");
        string csv = await file.Content.ReadAsStringAsync(Ct);
        csv.ShouldContain(row.GetProperty("code").GetString()!);
        csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length.ShouldBe(2);
    }

    // ----- Who can do what ------------------------------------------------------------------------

    [Fact]
    public async Task Outsiders_CannotSeeOrChangeServiceLevels()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid project = await SupportProjectAsync(admin, org.Manager);
        Guid task = await TicketAsync(admin, project);
        await MissTargetsAsync(task, DateTime.UtcNow.AddHours(-1));

        using HttpClient outsider = await factory.CreateScopedUserClientAsync(
            Email("sla-out"), org.SkipLevel, (Permissions.Project.TaskView, DataScope.Own), (Permissions.Project.TaskManage, DataScope.Own), (Permissions.Project.Manage, DataScope.Own), (Permissions.Project.FinancialsManage, DataScope.Own));

        (await outsider.GetAsync($"/api/v1/projects/{project}/service-levels?month={Iso(ThisMonth)}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await outsider.PutAsJsonAsync($"/api/v1/projects/{project}/service-levels/policy", Policy(), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await outsider.PostAsync($"/api/v1/projects/{project}/tickets/{task}/respond", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await outsider.PostAsJsonAsync($"/api/v1/projects/{project}/tickets/{task}/exclude", new { Reason = "Because" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        JsonElement[] rows = (await outsider.GetFromJsonAsync<JsonElement[]>($"/api/v1/projects/service-levels?month={Iso(ThisMonth)}", Ct))!;
        rows.ShouldNotContain(row => row.GetProperty("projectId").GetGuid() == project);
    }

    [Fact]
    public async Task OnlySomeoneWhoManagesFinancials_CanExcludeABreach()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid project = await SupportProjectAsync(admin, org.Manager);
        Guid task = await TicketAsync(admin, project);
        await MissTargetsAsync(task, DateTime.UtcNow.AddHours(-1));

        using HttpClient lead = await factory.CreateScopedUserClientAsync(Email("sla-lead"), org.Manager, (Permissions.Project.TaskView, DataScope.Own), (Permissions.Project.TaskManage, DataScope.Own));

        (await lead.PostAsJsonAsync($"/api/v1/projects/{project}/tickets/{task}/exclude", new { Reason = "Because" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
