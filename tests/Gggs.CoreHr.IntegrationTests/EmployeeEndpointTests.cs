using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Gggs.CoreHr.Api.Endpoints;
using Shouldly;

namespace Gggs.CoreHr.IntegrationTests;

/// <summary>
/// Employees through the real endpoints: hiring into a real org structure, moving through
/// transfer/promotion/confirmation/exit, the cross-entity 404s when an org reference does not
/// exist, and the "exactly one current employment record" invariant the database itself enforces
/// with a filtered unique index.
/// </summary>
[Collection(nameof(CoreHrApiCollection))]
public sealed class EmployeeEndpointTests(CoreHrApiFactory factory)
{
    /// <summary>
    /// The API's wire format (camelCase properties, string enums - see <see cref="Gggs.BuildingBlocks.Web.JsonConfiguration"/>).
    /// The default HttpClient JSON options know neither, so anything with an EmploymentType or a
    /// camelCase-only match needs this passed explicitly.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = BuildJsonOptions();

    private static JsonSerializerOptions BuildJsonOptions()
    {
        var options = new JsonSerializerOptions();
        Gggs.BuildingBlocks.Web.JsonConfiguration.Apply(options);
        return options;
    }

    [Fact]
    public async Task HiringAnEmployee_WithoutThePermission_IsForbidden()
    {
        using HttpClient client = factory.CreateClientFor(); // no permissions at all

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/employees",
            new
            {
                EmployeeCode = "NOPERM",
                FirstName = "Priya",
                DateOfJoining = new DateOnly(2026, 1, 15),
                Assignment = new { LegalEntityId = Guid.NewGuid(), BranchId = Guid.NewGuid(), DepartmentId = Guid.NewGuid(), DesignationId = Guid.NewGuid(), EmploymentType = 1 },
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task HiringAnEmployee_WithADepartmentThatDoesNotExist_ReturnsNotFound()
    {
        using HttpClient client = factory.CreateClientFor(
            Permissions.Employee.Create,
            Permissions.Organization.LegalEntityManage,
            Permissions.Organization.Manage,
            Permissions.Organization.DepartmentManage);

        OrgReferences org = await CreateOrgReferencesAsync(client);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/employees",
            new
            {
                EmployeeCode = $"EMP-{Guid.NewGuid():N}"[..12],
                FirstName = "Priya",
                LastName = "Sharma",
                DateOfJoining = new DateOnly(2026, 1, 15),
                Assignment = new
                {
                    LegalEntityId = org.LegalEntityId,
                    BranchId = org.BranchId,
                    DepartmentId = Guid.NewGuid(), // does not exist
                    DesignationId = org.DesignationId,
                    EmploymentType = 1,
                },
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task HiringAnEmployee_WithAllValidReferences_Succeeds()
    {
        using HttpClient client = factory.CreateClientFor(
            Permissions.Employee.Create,
            Permissions.Organization.LegalEntityManage,
            Permissions.Organization.Manage,
            Permissions.Organization.DepartmentManage);

        OrgReferences org = await CreateOrgReferencesAsync(client);

        EmployeeResponse employee = await HireAsync(client, org, "Priya", "Sharma", new DateOnly(2026, 1, 15));

        employee.DisplayName.ShouldBe("Priya Sharma");
        employee.Status.ShouldBe("PreBoarding");
    }

    [Fact]
    public async Task HiringTheSameEmployeeCodeTwice_IsRejectedWithItsErrorCode()
    {
        using HttpClient client = factory.CreateClientFor(
            Permissions.Employee.Create,
            Permissions.Organization.LegalEntityManage,
            Permissions.Organization.Manage,
            Permissions.Organization.DepartmentManage);

        OrgReferences org = await CreateOrgReferencesAsync(client);
        string code = $"DUP-{Guid.NewGuid():N}"[..12];

        await HireAsync(client, org, "Priya", "Sharma", new DateOnly(2026, 1, 15), code);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/employees",
            new
            {
                EmployeeCode = code,
                FirstName = "Someone",
                LastName = "Else",
                DateOfJoining = new DateOnly(2026, 1, 15),
                Assignment = ToAssignment(org),
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldContain("EMPLOYEE_CODE_IN_USE");
    }

    [Fact]
    public async Task TransferringAnEmployee_EndsTheOldRecordAndOpensANewOne()
    {
        using HttpClient client = factory.CreateClientFor(
            Permissions.Employee.Create,
            Permissions.Employee.EmploymentManage,
            Permissions.Employee.HistoryView,
            Permissions.Organization.LegalEntityManage,
            Permissions.Organization.Manage,
            Permissions.Organization.DepartmentManage);

        OrgReferences org = await CreateOrgReferencesAsync(client);
        EmployeeResponse employee = await HireAsync(client, org, "Priya", "Sharma", new DateOnly(2026, 1, 15));

        OrgReferences newDepartment = await CreateOrgReferencesAsync(client, sameLegalEntityAndBranchAs: org);
        DateOnly transferDate = new(2026, 4, 1);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/employees/{employee.Id}/transfer",
            new
            {
                EffectiveFrom = transferDate,
                Assignment = ToAssignment(newDepartment),
                Reason = "Reorg",
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        HttpResponseMessage historyResponse = await client.GetAsync(
            $"/api/v1/employees/{employee.Id}/employment", TestContext.Current.CancellationToken);
        List<EmploymentRecordResponse> history = (await historyResponse.Content.ReadFromJsonAsync<List<EmploymentRecordResponse>>(
            JsonOptions, TestContext.Current.CancellationToken))!;

        history.Count.ShouldBe(2);
        EmploymentRecordResponse current = history.Single(r => r.IsCurrent);
        EmploymentRecordResponse closed = history.Single(r => !r.IsCurrent);

        current.DepartmentId.ShouldBe(newDepartment.DepartmentId);
        current.ChangeType.ShouldBe("Transfer");
        closed.EffectiveTo.ShouldBe(transferDate.AddDays(-1));
    }

    [Fact]
    public async Task TransferringToAnAssignmentThatWouldMakeTheEmployeeTheirOwnManager_IsRejectedAndLeavesTheCurrentRecordIntact()
    {
        using HttpClient client = factory.CreateClientFor(
            Permissions.Employee.Create,
            Permissions.Employee.EmploymentManage,
            Permissions.Employee.HistoryView,
            Permissions.Organization.LegalEntityManage,
            Permissions.Organization.Manage,
            Permissions.Organization.DepartmentManage);

        OrgReferences org = await CreateOrgReferencesAsync(client);
        EmployeeResponse employee = await HireAsync(client, org, "Priya", "Sharma", new DateOnly(2026, 1, 15));

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/employees/{employee.Id}/transfer",
            new
            {
                EffectiveFrom = new DateOnly(2026, 4, 1),
                Assignment = ToAssignment(org, reportingManagerId: employee.Id),
                Reason = "Bad request",
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldContain("EMPLOYMENT_RECORD_CANNOT_REPORT_TO_SELF");

        HttpResponseMessage historyResponse = await client.GetAsync(
            $"/api/v1/employees/{employee.Id}/employment", TestContext.Current.CancellationToken);
        List<EmploymentRecordResponse> history = (await historyResponse.Content.ReadFromJsonAsync<List<EmploymentRecordResponse>>(
            JsonOptions, TestContext.Current.CancellationToken))!;

        // The rejected transfer must not have closed the original record - exactly one still current.
        history.Count.ShouldBe(1);
        history.Single().IsCurrent.ShouldBeTrue();
        history.Single().EffectiveTo.ShouldBeNull();
    }

    [Fact]
    public async Task OnlyOneEmploymentRecordCanBeCurrentAtATime_EnforcedEndToEnd()
    {
        // The database itself enforces this (a filtered unique index on IsCurrent = 1), so this
        // checks it the way a caller would ever observe it - through the history endpoint - rather
        // than reaching past the API into the database directly.
        using HttpClient client = factory.CreateClientFor(
            Permissions.Employee.Create,
            Permissions.Employee.EmploymentManage,
            Permissions.Employee.HistoryView,
            Permissions.Organization.LegalEntityManage,
            Permissions.Organization.Manage,
            Permissions.Organization.DepartmentManage);

        OrgReferences org = await CreateOrgReferencesAsync(client);
        EmployeeResponse employee = await HireAsync(client, org, "Priya", "Sharma", new DateOnly(2026, 1, 15));

        HttpResponseMessage promoteResponse = await client.PostAsJsonAsync(
            $"/api/v1/employees/{employee.Id}/promote",
            new { EffectiveFrom = new DateOnly(2026, 6, 1), Assignment = ToAssignment(org), Reason = "Promotion" },
            TestContext.Current.CancellationToken);
        promoteResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        HttpResponseMessage historyResponse = await client.GetAsync(
            $"/api/v1/employees/{employee.Id}/employment", TestContext.Current.CancellationToken);
        List<EmploymentRecordResponse> history = (await historyResponse.Content.ReadFromJsonAsync<List<EmploymentRecordResponse>>(
            JsonOptions, TestContext.Current.CancellationToken))!;

        history.Count.ShouldBe(2);
        history.Count(r => r.IsCurrent).ShouldBe(1);
    }

    [Fact]
    public async Task ConfirmingThenExitingAnEmployee_MovesThroughTheExpectedStatuses()
    {
        using HttpClient client = factory.CreateClientFor(
            Permissions.Employee.Create,
            Permissions.Employee.EmploymentManage,
            Permissions.Employee.ExitManage,
            Permissions.Organization.LegalEntityManage,
            Permissions.Organization.Manage,
            Permissions.Organization.DepartmentManage);

        OrgReferences org = await CreateOrgReferencesAsync(client);
        DateOnly joining = new(2026, 1, 15);
        EmployeeResponse employee = await HireAsync(client, org, "Priya", "Sharma", joining);

        HttpResponseMessage confirmResponse = await client.PostAsJsonAsync(
            $"/api/v1/employees/{employee.Id}/confirm",
            new { ConfirmationDate = joining.AddMonths(3) },
            TestContext.Current.CancellationToken);
        confirmResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        EmployeeResponse confirmed = (await confirmResponse.Content.ReadFromJsonAsync<EmployeeResponse>(
            TestContext.Current.CancellationToken))!;
        confirmed.Status.ShouldBe("Confirmed");

        HttpResponseMessage exitResponse = await client.PostAsJsonAsync(
            $"/api/v1/employees/{employee.Id}/exit",
            new { ExitDate = joining.AddYears(1), WasAbsconded = false },
            TestContext.Current.CancellationToken);
        exitResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        EmployeeResponse exited = (await exitResponse.Content.ReadFromJsonAsync<EmployeeResponse>(
            TestContext.Current.CancellationToken))!;
        exited.Status.ShouldBe("Exited");
        exited.ExitDate.ShouldBe(joining.AddYears(1));
    }

    [Fact]
    public async Task ExitingAnEmployeeTwice_IsRejectedWithItsErrorCode()
    {
        using HttpClient client = factory.CreateClientFor(
            Permissions.Employee.Create,
            Permissions.Employee.ExitManage,
            Permissions.Organization.LegalEntityManage,
            Permissions.Organization.Manage,
            Permissions.Organization.DepartmentManage);

        OrgReferences org = await CreateOrgReferencesAsync(client);
        DateOnly joining = new(2026, 1, 15);
        EmployeeResponse employee = await HireAsync(client, org, "Priya", "Sharma", joining);

        await client.PostAsJsonAsync(
            $"/api/v1/employees/{employee.Id}/exit",
            new { ExitDate = joining.AddYears(1), WasAbsconded = false },
            TestContext.Current.CancellationToken);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/employees/{employee.Id}/exit",
            new { ExitDate = joining.AddYears(1).AddDays(1), WasAbsconded = false },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldContain("EMPLOYEE_ALREADY_LEFT");
    }

    [Fact]
    public async Task ListMyTeam_ReturnsEmployeesReportingToTheCaller()
    {
        string[] permissions =
        [
            Permissions.Employee.Create,
            Permissions.Organization.LegalEntityManage,
            Permissions.Organization.Manage,
            Permissions.Organization.DepartmentManage,
            Permissions.Employee.View,
        ];

        using HttpClient setupClient = factory.CreateClientFor(permissions);

        OrgReferences org = await CreateOrgReferencesAsync(setupClient);
        DateOnly joining = new(2026, 1, 15);

        // The caller has to exist as a real Employee row before anything can report to them -
        // EmployeeService's own reference check for ReportingManagerId requires it.
        EmployeeResponse manager = await HireAsync(setupClient, org, "The", "Manager", joining);

        using HttpClient client = factory.CreateClientForEmployee(manager.Id, permissions);

        EmployeeResponse report = await HireAsync(client, org, "Priya", "Sharma", joining, reportingManagerId: manager.Id);

        // Someone reporting to a different manager entirely - must not show up in the caller's team.
        EmployeeResponse otherManager = await HireAsync(client, org, "Other", "Manager", joining);
        await HireAsync(client, org, "Not", "OnMyTeam", joining, reportingManagerId: otherManager.Id);

        HttpResponseMessage response = await client.GetAsync("/api/v1/employees/my-team", TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        List<EmployeeListItem>? team = await response.Content.ReadFromJsonAsync<List<EmployeeListItem>>(
            JsonOptions, TestContext.Current.CancellationToken);

        team.ShouldNotBeNull();
        team.ShouldContain(x => x.Id == report.Id);
        team.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ListMyTeam_WithNoLinkedEmployee_ReturnsAnEmptyList()
    {
        // CreateClientFor (not CreateClientForEmployee) issues no "emp" claim at all - an admin
        // account with no employee profile of their own, same as ICurrentUser.EmployeeId's own
        // remarks on why it is nullable.
        using HttpClient client = factory.CreateClientFor(Permissions.Employee.View);

        HttpResponseMessage response = await client.GetAsync("/api/v1/employees/my-team", TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        List<EmployeeListItem>? team = await response.Content.ReadFromJsonAsync<List<EmployeeListItem>>(
            JsonOptions, TestContext.Current.CancellationToken);

        team.ShouldNotBeNull();
        team.ShouldBeEmpty();
    }

    private static object ToAssignment(OrgReferences org, Guid? reportingManagerId = null) => new
    {
        LegalEntityId = org.LegalEntityId,
        BranchId = org.BranchId,
        DepartmentId = org.DepartmentId,
        DesignationId = org.DesignationId,
        ReportingManagerId = reportingManagerId,
        EmploymentType = 1,
    };

    private static async Task<EmployeeResponse> HireAsync(
        HttpClient client, OrgReferences org, string firstName, string lastName, DateOnly dateOfJoining,
        string? code = null, Guid? reportingManagerId = null)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/employees",
            new
            {
                EmployeeCode = code ?? $"EMP-{Guid.NewGuid():N}"[..12],
                FirstName = firstName,
                LastName = lastName,
                DateOfJoining = dateOfJoining,
                Assignment = ToAssignment(org, reportingManagerId),
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<EmployeeResponse>(TestContext.Current.CancellationToken))!;
    }

    /// <summary>Legal entity, branch, department and designation ids real enough for EmployeeService's reference checks to accept.</summary>
    private sealed record OrgReferences(Guid LegalEntityId, Guid BranchId, Guid DepartmentId, Guid DesignationId);

    private static async Task<OrgReferences> CreateOrgReferencesAsync(HttpClient client, OrgReferences? sameLegalEntityAndBranchAs = null)
    {
        Guid legalEntityId;
        Guid branchId;

        if (sameLegalEntityAndBranchAs is { } shared)
        {
            legalEntityId = shared.LegalEntityId;
            branchId = shared.BranchId;
        }
        else
        {
            HttpResponseMessage legalEntityResponse = await client.PostAsJsonAsync(
                "/api/v1/legal-entities",
                new { Code = $"LE-{Guid.NewGuid():N}"[..12], Name = "Acme Corp", CountryCode = "IN", Currency = "INR" },
                TestContext.Current.CancellationToken);
            LegalEntityResponse legalEntity = (await legalEntityResponse.Content.ReadFromJsonAsync<LegalEntityResponse>(
                TestContext.Current.CancellationToken))!;
            legalEntityId = legalEntity.Id;

            HttpResponseMessage branchResponse = await client.PostAsJsonAsync(
                "/api/v1/branches",
                new { Code = $"BR-{Guid.NewGuid():N}"[..12], Name = "Head Office", LegalEntityId = legalEntityId, CountryCode = "IN", TimeZone = "Asia/Kolkata" },
                TestContext.Current.CancellationToken);
            BranchResponse branch = (await branchResponse.Content.ReadFromJsonAsync<BranchResponse>(
                TestContext.Current.CancellationToken))!;
            branchId = branch.Id;
        }

        HttpResponseMessage departmentResponse = await client.PostAsJsonAsync(
            "/api/v1/departments",
            new { Code = $"DEPT-{Guid.NewGuid():N}"[..12], Name = "Engineering", ParentDepartmentId = (Guid?)null },
            TestContext.Current.CancellationToken);
        DepartmentResponse department = (await departmentResponse.Content.ReadFromJsonAsync<DepartmentResponse>(
            TestContext.Current.CancellationToken))!;

        HttpResponseMessage designationResponse = await client.PostAsJsonAsync(
            "/api/v1/designations",
            new { Code = $"DSG-{Guid.NewGuid():N}"[..12], Title = "Engineer", JobFamily = (string?)null, JobLevel = (int?)null },
            TestContext.Current.CancellationToken);
        DesignationResponse designation = (await designationResponse.Content.ReadFromJsonAsync<DesignationResponse>(
            TestContext.Current.CancellationToken))!;

        return new OrgReferences(legalEntityId, branchId, department.Id, designation.Id);
    }
}
