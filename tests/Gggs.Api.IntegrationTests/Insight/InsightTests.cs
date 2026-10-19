using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Shouldly;
using static Gggs.Api.IntegrationTests.DeliveryApi;
using static Gggs.Api.IntegrationTests.PlatformServicesApi;

namespace Gggs.Api.IntegrationTests;

/// <summary>
/// M11 end to end: dashboards assembled from every module within report access, the executive view,
/// global search, the HR calendar, the import framework and integrations with secrets kept secret.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class InsightTests(PeopleOsApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static decimal Value(JsonElement dashboard, string key)
        => dashboard.GetProperty("metrics").EnumerateArray().Single(metric => metric.GetProperty("key").GetString() == key).GetProperty("value").GetDecimal();

    [Theory]
    [InlineData("executive")]
    [InlineData("people")]
    [InlineData("workforce")]
    [InlineData("time")]
    [InlineData("projects")]
    [InlineData("resources")]
    [InlineData("payroll")]
    [InlineData("hiring")]
    [InlineData("talent")]
    [InlineData("experience")]
    public async Task Every_dashboard_is_assembled_for_an_administrator(string area)
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        JsonElement dashboard = await OkAsync(await admin.GetAsync($"/api/v1/analytics/{area}", Ct));
        dashboard.GetProperty("tenantWide").GetBoolean().ShouldBeTrue();
        dashboard.GetProperty("metrics").GetArrayLength().ShouldBeGreaterThan(0);
        if (area == "executive")
        {
            JsonElement[] metrics = [.. dashboard.GetProperty("metrics").EnumerateArray()];
            metrics.ShouldAllBe(metric => metric.GetProperty("executive").GetBoolean());
            metrics.Select(metric => metric.GetProperty("area").GetString()).Distinct().Count().ShouldBeGreaterThan(3);
        }
    }

    [Fact]
    public async Task A_manager_sees_people_metrics_for_their_reports_only_and_no_executive_view()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid department = await CoreHrApi.DepartmentAsync(admin);
        Guid manager = await CoreHrApi.HireAsync(admin, department);
        await CoreHrApi.HireAsync(admin, department, manager);
        await CoreHrApi.HireAsync(admin, department, manager);
        using HttpClient lead = await factory.CreateScopedUserClientAsync(Email("lead"), manager, (Permissions.Report.View, DataScope.DirectReports));

        JsonElement areas = await OkAsync(await lead.GetAsync("/api/v1/analytics", Ct));
        areas.EnumerateArray().ShouldNotContain(area => area.GetProperty("key").GetString() == "executive");
        JsonElement people = await OkAsync(await lead.GetAsync("/api/v1/analytics/people", Ct));
        people.GetProperty("tenantWide").GetBoolean().ShouldBeFalse();
        Value(people, "people.headcount").ShouldBe(3m); // The manager and their two reports.
        (await lead.GetAsync("/api/v1/analytics/executive", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Company-wide sources stay silent for a scoped viewer rather than leaking totals.
        JsonElement payroll = await OkAsync(await lead.GetAsync("/api/v1/analytics/payroll", Ct));
        payroll.GetProperty("metrics").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Search_finds_people_and_projects_the_caller_may_see()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid employee = await CoreHrApi.HireAsync(admin, await CoreHrApi.DepartmentAsync(admin));
        string name = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/employees/{employee}", Ct)).GetProperty("displayName").GetString()!;
        string term = name.Split(' ')[^1];

        JsonElement results = await OkAsync(await admin.GetAsync($"/api/v1/search?q={Uri.EscapeDataString(term)}", Ct));
        results.GetProperty("hits").EnumerateArray().ShouldContain(hit => hit.GetProperty("kind").GetString() == "employee" && hit.GetProperty("id").GetGuid() == employee);
        (await OkAsync(await admin.GetAsync("/api/v1/search?q=a", Ct))).GetProperty("hits").GetArrayLength().ShouldBe(0);

        Guid self = await CoreHrApi.HireAsync(admin, await CoreHrApi.DepartmentAsync(admin));
        using HttpClient employeeClient = await factory.CreateScopedUserClientAsync(Email("plain"), self, (Permissions.Self.ProfileView, DataScope.Own));
        JsonElement mine = await OkAsync(await employeeClient.GetAsync($"/api/v1/search?q={Uri.EscapeDataString(term)}", Ct));
        mine.GetProperty("hits").EnumerateArray().ShouldNotContain(hit => hit.GetProperty("kind").GetString() == "employee");
    }

    [Fact]
    public async Task The_calendar_shows_anniversaries_within_reach_and_keeps_to_three_months()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid employee = await CoreHrApi.HireAsync(admin, await CoreHrApi.DepartmentAsync(admin));

        // Every test hire joined on 15 January 2024.
        JsonElement calendar = await OkAsync(await admin.GetAsync("/api/v1/calendar?from=2027-01-01&to=2027-01-31&scope=all", Ct));
        calendar.GetProperty("entries").EnumerateArray().ShouldContain(entry => entry.GetProperty("kind").GetString() == "anniversary"
            && entry.GetProperty("employeeId").GetGuid() == employee && entry.GetProperty("start").GetString() == "2027-01-15");
        (await admin.GetAsync("/api/v1/calendar?from=2027-01-01&to=2027-06-30", Ct)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        using HttpClient plain = await factory.CreateScopedUserClientAsync(Email("cal"), employee, (Permissions.Self.ProfileView, DataScope.Own));
        (await plain.GetAsync("/api/v1/calendar?from=2027-01-01&to=2027-01-31&scope=all", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        JsonElement mine = await OkAsync(await plain.GetAsync("/api/v1/calendar?from=2027-01-01&to=2027-01-31", Ct));
        mine.GetProperty("entries").EnumerateArray().ShouldAllBe(entry => entry.GetProperty("kind").GetString() == "holiday" || entry.GetProperty("employeeId").GetGuid() == employee);
    }

    [Fact]
    public async Task An_import_previews_every_row_and_commits_only_the_valid_ones()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        JsonElement handlers = await OkAsync(await admin.GetAsync("/api/v1/imports", Ct));
        handlers.EnumerateArray().Select(handler => handler.GetProperty("key").GetString()).ShouldContain("holidays");
        string template = await (await admin.GetAsync("/api/v1/imports/holidays/template", Ct)).Content.ReadAsStringAsync(Ct);
        template.ShouldStartWith("calendar,date,name,optional");

        int year = 2030 + Random.Shared.Next(0, 60);
        string csv = $"calendar,date,name,optional\nGENERAL,{year}-08-15,Independence Day,no\nGENERAL,{year}-13-01,Bad month,no\nNOPE,{year}-10-02,\"Gandhi Jayanti, observed\",yes\n";
        JsonElement preview = await OkAsync(await admin.PostAsJsonAsync("/api/v1/imports/holidays/preview", new { Csv = csv, FileName = "holidays.csv" }, Ct));
        preview.GetProperty("rows").GetInt32().ShouldBe(3);
        preview.GetProperty("valid").GetInt32().ShouldBe(1);
        preview.GetProperty("imported").GetInt32().ShouldBe(0);

        JsonElement committed = await OkAsync(await admin.PostAsJsonAsync("/api/v1/imports/holidays", new { Csv = csv, FileName = "holidays.csv" }, Ct));
        committed.GetProperty("imported").GetInt32().ShouldBe(1);
        JsonElement again = await OkAsync(await admin.PostAsJsonAsync("/api/v1/imports/holidays/preview", new { Csv = csv, FileName = "holidays.csv" }, Ct));
        again.GetProperty("valid").GetInt32().ShouldBe(0);
        (await OkAsync(await admin.GetAsync("/api/v1/imports/history", Ct))).EnumerateArray().ShouldContain(run => run.GetProperty("handlerKey").GetString() == "holidays");

        HttpResponseMessage missing = await admin.PostAsJsonAsync("/api/v1/imports/assets/preview", new { Csv = "name\nLaptop\n", FileName = "a.csv" }, Ct);
        (await CoreHrApi.ErrorCodeAsync(missing)).ShouldBe("IMPORT_COLUMNS");
        JsonElement assets = await OkAsync(await admin.PostAsJsonAsync("/api/v1/imports/assets", new { Csv = $"category,name,serialNumber,purchaseCost\nLAPTOP,Imported laptop,IMP-{Guid.NewGuid():N},50000\n", FileName = "a.csv" }, Ct));
        assets.GetProperty("imported").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task Integration_secrets_are_kept_encrypted_and_never_returned()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        JsonElement adapters = await OkAsync(await admin.GetAsync("/api/v1/integrations/adapters", Ct));
        string[] keys = [.. adapters.EnumerateArray().Select(adapter => adapter.GetProperty("key").GetString()!)];
        keys.ShouldContain("bank-transfer");
        keys.ShouldContain("accounting-journal");
        keys.ShouldContain("entra-id");
        keys.ShouldContain("webhook");

        string name = $"Bank {Guid.NewGuid():N}"[..12];
        JsonElement bank = await OkAsync(await admin.PostAsJsonAsync("/api/v1/integrations/connections", new
        {
            AdapterKey = "bank-transfer",
            Name = name,
            Settings = new Dictionary<string, string> { ["bankName"] = "HDFC Bank", ["debitAccount"] = "50100987654321", ["ifsc"] = "HDFC0001234" },
        }, Ct));
        Guid id = bank.GetProperty("id").GetGuid();
        string shown = bank.GetProperty("settings").GetProperty("debitAccount").GetString()!;
        shown.ShouldNotContain("5010098765");
        shown.ShouldEndWith("21");

        // Saving again without the secret keeps it.
        await OkAsync(await admin.PutAsJsonAsync($"/api/v1/integrations/connections/{id}", new { AdapterKey = "bank-transfer", Name = name, Settings = new Dictionary<string, string> { ["bankName"] = "HDFC Bank", ["ifsc"] = "HDFC0001234" } }, Ct));
        JsonElement tested = await OkAsync(await admin.PostAsync($"/api/v1/integrations/connections/{id}/test", null, Ct));
        tested.GetProperty("lastTestSucceeded").GetBoolean().ShouldBeTrue();

        JsonElement run = await OkAsync(await admin.PostAsJsonAsync($"/api/v1/integrations/connections/{id}/run", new { Operation = "salary-file", Parameters = new Dictionary<string, string> { ["period"] = "1999-01" } }, Ct));
        run.GetProperty("succeeded").GetBoolean().ShouldBeFalse();
        run.GetProperty("message").GetString()!.ShouldContain("no finalized payroll");
        (await CoreHrApi.ErrorCodeAsync(await admin.PostAsJsonAsync($"/api/v1/integrations/connections/{id}/run", new { Operation = "salary-file" }, Ct))).ShouldBe("INTEGRATION_PARAMETER");
        (await OkAsync(await admin.GetAsync($"/api/v1/integrations/runs?connectionId={id}", Ct))).GetArrayLength().ShouldBe(1);

        JsonElement hook = await OkAsync(await admin.PostAsJsonAsync("/api/v1/integrations/connections", new
        {
            AdapterKey = "webhook",
            Name = $"Hook {Guid.NewGuid():N}"[..12],
            Settings = new Dictionary<string, string> { ["url"] = "http://insecure.example/hook", ["secret"] = "s3cret" },
        }, Ct));
        JsonElement hookTest = await OkAsync(await admin.PostAsync($"/api/v1/integrations/connections/{hook.GetProperty("id").GetGuid()}/test", null, Ct));
        hookTest.GetProperty("lastTestSucceeded").GetBoolean().ShouldBeFalse();
        hookTest.GetProperty("lastMessage").GetString()!.ShouldContain("https");
    }

    [Fact]
    public async Task Report_digests_are_subscribed_and_unsubscribed()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        JsonElement subscribed = await OkAsync(await admin.PutAsJsonAsync("/api/v1/analytics/subscriptions", new { Area = "executive", Frequency = "Weekly" }, Ct));
        JsonElement subscription = subscribed.EnumerateArray().Single(row => row.GetProperty("area").GetString() == "executive");
        await OkAsync(await admin.PutAsJsonAsync("/api/v1/analytics/subscriptions", new { Area = "executive", Frequency = "Monthly" }, Ct));
        JsonElement[] mine = (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/analytics/subscriptions", Ct))!;
        mine.Count(row => row.GetProperty("area").GetString() == "executive").ShouldBe(1);
        mine.Single(row => row.GetProperty("area").GetString() == "executive").GetProperty("frequency").GetString().ShouldBe("Monthly");
        (await CoreHrApi.ErrorCodeAsync(await admin.PutAsJsonAsync("/api/v1/analytics/subscriptions", new { Area = "nonsense", Frequency = "Weekly" }, Ct))).ShouldBe("DIGEST_AREA");
        await OkAsync(await admin.DeleteAsync($"/api/v1/analytics/subscriptions/{subscription.GetProperty("id").GetGuid()}", Ct));
        (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/analytics/subscriptions", Ct))!.ShouldNotContain(row => row.GetProperty("area").GetString() == "executive");
    }
}
