using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Shouldly;

namespace Gggs.Api.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class EmployeeTests(PeopleOsApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task HiringIntoAPosition_TakesItsJobAndDepartment_AndAFullSeatRefusesTheNext()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid department = await CoreHrApi.DepartmentAsync(admin);
        Guid job = await CoreHrApi.JobAsync(admin);
        Guid position = await CoreHrApi.PositionAsync(admin, job, department);

        Guid hired = await CoreHrApi.HireAsync(admin, departmentId: department, positionId: position);
        JsonElement detail = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/employees/{hired}", Ct))!;
        detail.GetProperty("assignment").GetProperty("job").GetProperty("id").GetGuid().ShouldBe(job);
        detail.GetProperty("employeeCode").GetString().ShouldStartWith("EMP");

        HttpResponseMessage second = await CoreHrApi.HireRawAsync(admin, department, positionId: position);
        second.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CoreHrApi.ErrorCodeAsync(second)).ShouldBe("EMPLOYEE_POSITION_FULL");
    }

    [Fact]
    public async Task ATransfer_ClosesTheOldRecordTheDayBeforeAndOpensANewOne()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid from = await CoreHrApi.DepartmentAsync(admin);
        Guid to = await CoreHrApi.DepartmentAsync(admin);
        Guid employee = await CoreHrApi.HireAsync(admin, from);

        HttpResponseMessage transfer = await admin.PostAsJsonAsync($"/api/v1/employees/{employee}/assignment-changes", new
        {
            ChangeType = "Transfer",
            EffectiveFrom = "2024-06-01",
            Reason = "Team restructure",
            Assignment = new { DepartmentId = to, EmploymentTypeCode = "FULL_TIME", WorkModeCode = "OFFICE", Designation = "Engineer" },
        }, Ct);
        transfer.StatusCode.ShouldBe(HttpStatusCode.Created, await transfer.Content.ReadAsStringAsync(Ct));

        JsonElement[] history = (await admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/employees/{employee}/history", Ct))!;
        history.Length.ShouldBe(2);
        history[0].GetProperty("changeType").GetString().ShouldBe("Transfer");
        (history[0].TryGetProperty("effectiveTo", out JsonElement openEnd) ? openEnd.ValueKind : JsonValueKind.Null).ShouldBe(JsonValueKind.Null, "the current record is open-ended");
        history[1].GetProperty("effectiveTo").GetString().ShouldBe("2024-05-31");

        JsonElement detail = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/employees/{employee}", Ct))!;
        detail.GetProperty("assignment").GetProperty("department").GetProperty("id").GetGuid().ShouldBe(to);
    }

    [Fact]
    public async Task AFutureDatedChange_IsRefusedForNow()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid department = await CoreHrApi.DepartmentAsync(admin);
        Guid employee = await CoreHrApi.HireAsync(admin, department);

        HttpResponseMessage response = await admin.PostAsJsonAsync($"/api/v1/employees/{employee}/assignment-changes", new
        {
            ChangeType = "Redesignation",
            EffectiveFrom = DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd"),
            Assignment = new { DepartmentId = department, EmploymentTypeCode = "FULL_TIME", WorkModeCode = "OFFICE", Designation = "Senior engineer" },
        }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CoreHrApi.ErrorCodeAsync(response)).ShouldBe("EMPLOYEE_CHANGE_IN_FUTURE");
    }

    [Fact]
    public async Task AReportingLine_CannotLoop()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid department = await CoreHrApi.DepartmentAsync(admin);
        Guid boss = await CoreHrApi.HireAsync(admin, department);
        Guid report = await CoreHrApi.HireAsync(admin, department, managerId: boss);

        HttpResponseMessage response = await admin.PostAsJsonAsync($"/api/v1/employees/{boss}/assignment-changes", new
        {
            ChangeType = "ManagerChange",
            EffectiveFrom = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            Assignment = new { DepartmentId = department, ManagerEmployeeId = report, EmploymentTypeCode = "FULL_TIME", WorkModeCode = "OFFICE" },
        }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CoreHrApi.ErrorCodeAsync(response)).ShouldBe("EMPLOYEE_MANAGER_CYCLE");
    }

    [Fact]
    public async Task StatutoryIdentifiers_AreMaskedOnTheProfile_AndRevealedOnlyWithThePermission()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid department = await CoreHrApi.DepartmentAsync(admin);
        Guid employee = await CoreHrApi.HireAsync(admin, department);

        (await admin.PutAsJsonAsync($"/api/v1/employees/{employee}/statutory", new { Pan = "abcde1234f", Uan = "100200300400" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        JsonElement detail = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/employees/{employee}", Ct))!;
        detail.GetProperty("sensitive").GetProperty("panHint").GetString().ShouldBe("XXXXXX234F");
        detail.GetRawText().ShouldNotContain("ABCDE1234F");

        JsonElement revealed = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/employees/{employee}/statutory", Ct))!;
        revealed.GetProperty("pan").GetString().ShouldBe("ABCDE1234F");

        using HttpClient hr = factory.CreateClientFor(PeopleOsApiFactory.SeededTenantId, Permissions.Employee.View);
        (await hr.GetAsync($"/api/v1/employees/{employee}/statutory", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AMalformedPan_IsRefusedBeforeAnythingIsStored()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid employee = await CoreHrApi.HireAsync(admin, await CoreHrApi.DepartmentAsync(admin));

        HttpResponseMessage response = await admin.PutAsJsonAsync($"/api/v1/employees/{employee}/statutory", new { Pan = "1234" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CoreHrApi.ErrorCodeAsync(response)).ShouldBe("EMPLOYEE_FIELD_INVALID");
    }

    [Fact]
    public async Task BankDetails_RoundTripEncrypted()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid employee = await CoreHrApi.HireAsync(admin, await CoreHrApi.DepartmentAsync(admin));

        (await admin.PutAsJsonAsync($"/api/v1/employees/{employee}/bank", new { BankName = "HDFC Bank", Ifsc = "HDFC0001234", AccountHolder = "Test", AccountNumber = "50100123456789" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        JsonElement detail = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/employees/{employee}", Ct))!;
        detail.GetProperty("sensitive").GetProperty("bankAccountHint").GetString().ShouldBe("XXXXXXXXXX6789");

        JsonElement bank = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/employees/{employee}/bank", Ct))!;
        bank.GetProperty("accountNumber").GetString().ShouldBe("50100123456789");
    }

    [Fact]
    public async Task AManagerWithReports_CannotExitUntilTheyAreMoved()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid department = await CoreHrApi.DepartmentAsync(admin);
        Guid boss = await CoreHrApi.HireAsync(admin, department);
        await CoreHrApi.HireAsync(admin, department, managerId: boss);

        HttpResponseMessage response = await admin.PostAsJsonAsync($"/api/v1/employees/{boss}/exit", new { ExitDate = "2025-01-31", ExitReasonCode = "RESIGNATION" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CoreHrApi.ErrorCodeAsync(response)).ShouldBe("EMPLOYEE_HAS_REPORTS");
    }

    [Fact]
    public async Task AnExitedEmployee_LeavesTheDirectoryButKeepsTheirHistory()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid department = await CoreHrApi.DepartmentAsync(admin);
        Guid employee = await CoreHrApi.HireAsync(admin, department);

        (await admin.PostAsJsonAsync($"/api/v1/employees/{employee}/exit", new { ExitDate = "2025-01-31", ExitReasonCode = "RESIGNATION" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        JsonElement page = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/employees?departmentId={department}", Ct))!;
        page.GetProperty("items").GetArrayLength().ShouldBe(0);

        JsonElement withExited = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/employees?departmentId={department}&includeExited=true", Ct))!;
        withExited.GetProperty("items").GetArrayLength().ShouldBe(1);

        JsonElement[] history = (await admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/employees/{employee}/history", Ct))!;
        history.ShouldHaveSingleItem().GetProperty("effectiveTo").GetString().ShouldBe("2025-01-31");
    }

    [Fact]
    public async Task Employee360_MapsEverySection_AndLightsUpOnlyWhatIsBuilt()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid employee = await CoreHrApi.HireAsync(admin, await CoreHrApi.DepartmentAsync(admin));

        JsonElement view = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/employees/{employee}/360", Ct))!;
        JsonElement[] sections = [.. view.GetProperty("sections").EnumerateArray()];

        sections.Single(section => section.GetProperty("key").GetString() == "employment").GetProperty("available").GetBoolean().ShouldBeTrue();
        sections.Single(section => section.GetProperty("key").GetString() == "payroll").GetProperty("available").GetBoolean().ShouldBeTrue();
        // Performance is built (no milestone) but this tenant's plan does not license it.
        JsonElement performance = sections.Single(section => section.GetProperty("key").GetString() == "performance");
        performance.GetProperty("available").GetBoolean().ShouldBeFalse();
        performance.TryGetProperty("milestone", out _).ShouldBeFalse();
        JsonElement rewards = sections.Single(section => section.GetProperty("key").GetString() == "rewards");
        rewards.GetProperty("available").GetBoolean().ShouldBeFalse();
        rewards.GetProperty("milestone").GetString().ShouldBe("M10");
    }

    [Fact]
    public async Task AManagerScopedToDirectReports_SeesTheirTeamAndNoOneElse()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid department = await CoreHrApi.DepartmentAsync(admin);
        Guid manager = await CoreHrApi.HireAsync(admin, department);
        Guid report = await CoreHrApi.HireAsync(admin, department, managerId: manager);
        Guid stranger = await CoreHrApi.HireAsync(admin, department);

        using HttpClient managerClient = await factory.CreateScopedUserClientAsync(
            $"{CoreHrApi.Unique("mgr").ToLowerInvariant()}@testco.example",
            manager,
            (Permissions.Employee.View, DataScope.DirectReports));

        JsonElement page = (await managerClient.GetFromJsonAsync<JsonElement>("/api/v1/employees?pageSize=200", Ct))!;
        Guid[] visible = [.. page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid())];

        visible.ShouldBe([manager, report], ignoreOrder: true);
        (await managerClient.GetAsync($"/api/v1/employees/{stranger}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AnotherTenantsEmployee_IsNotFound()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid employee = await CoreHrApi.HireAsync(admin, await CoreHrApi.DepartmentAsync(admin));

        using HttpClient other = factory.CreateClientFor(PeopleOsApiFactory.OtherTenantId, Permissions.Employee.View);
        (await other.GetAsync($"/api/v1/employees/{employee}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ImportPreview_ReportsEveryBadRow_AndCommitIsAllOrNothing()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        string departmentCode = CoreHrApi.Unique("IMP");
        await CoreHrApi.DepartmentAsync(admin, code: departmentCode);
        string managerCode = CoreHrApi.Unique("M");

        object Row(string? code, string? first, string date, string department, string? manager = null)
            => new { EmployeeCode = code, FirstName = first, LastName = "Imported", JoiningDate = date, DepartmentCode = department, ManagerEmployeeCode = manager, EmploymentTypeCode = "FULL_TIME", WorkModeCode = "OFFICE" };

        var bad = new
        {
            Rows = new[]
            {
                Row(managerCode, "Lead", "2024-02-01", departmentCode),
                Row(null, "Report", "2024-02-01", departmentCode, managerCode),
                Row(null, null, "not-a-date", "NO-SUCH-DEPT"),
            },
        };

        JsonElement preview = (await (await admin.PostAsJsonAsync("/api/v1/employees/import/preview", bad, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct))!;
        preview.GetProperty("committed").GetBoolean().ShouldBeFalse();
        preview.GetProperty("succeeded").GetInt32().ShouldBe(2, "the second row's manager comes from the first row");
        preview.GetProperty("rows")[2].GetProperty("errors").GetArrayLength().ShouldBe(3);

        JsonElement failedCommit = (await (await admin.PostAsJsonAsync("/api/v1/employees/import", bad, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct))!;
        failedCommit.GetProperty("committed").GetBoolean().ShouldBeFalse();
        (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/employees?search={managerCode}", Ct))!.GetProperty("items").GetArrayLength().ShouldBe(0, "nothing is kept when a row fails");

        var good = new { Rows = bad.Rows.Take(2).ToArray() };
        JsonElement committed = (await (await admin.PostAsJsonAsync("/api/v1/employees/import", good, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct))!;
        committed.GetProperty("committed").GetBoolean().ShouldBeTrue();
        (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/employees?search={managerCode}", Ct))!.GetProperty("items").GetArrayLength().ShouldBe(1);
    }
}
