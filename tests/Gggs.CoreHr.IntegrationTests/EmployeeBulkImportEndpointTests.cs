using System.Net;
using System.Net.Http.Json;
using Gggs.BuildingBlocks.Security;
using Gggs.CoreHr.Api.Endpoints;
using Shouldly;

namespace Gggs.CoreHr.IntegrationTests;

/// <summary>
/// POST /api/v1/employees/bulk - the synchronous, ≤500-row import that resolves organizational
/// references by code (as a spreadsheet import actually has them) instead of by GUID, then hires
/// each row through the same <c>EmployeeService.HireAsync</c> path a single hire uses.
/// </summary>
[Collection(nameof(CoreHrApiCollection))]
public sealed class EmployeeBulkImportEndpointTests(CoreHrApiFactory factory)
{
    [Fact]
    public async Task HiringInBulk_WithoutThePermission_IsForbidden()
    {
        using HttpClient client = factory.CreateClientFor(); // no permissions at all

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/employees/bulk",
            new { Rows = Array.Empty<object>() },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task HiringInBulk_WithAllValidRows_HiresEveryRowAndReturnsTheirIds()
    {
        using HttpClient client = factory.CreateClientFor(
            Permissions.Employee.Create,
            Permissions.Organization.LegalEntityManage,
            Permissions.Organization.Manage,
            Permissions.Organization.DepartmentManage);

        OrgCodes org = await CreateOrgCodesAsync(client);
        string codeA = $"BLK-{Guid.NewGuid():N}"[..12];
        string codeB = $"BLK-{Guid.NewGuid():N}"[..12];

        var response = await client.PostAsJsonAsync(
            "/api/v1/employees/bulk",
            new
            {
                Rows = new[]
                {
                    BulkRow(codeA, "Priya", "Sharma", org),
                    BulkRow(codeB, "Raj", "Verma", org),
                },
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        BulkHireResultResponse result = (await response.Content.ReadFromJsonAsync<BulkHireResultResponse>(
            TestContext.Current.CancellationToken))!;

        result.TotalRows.ShouldBe(2);
        result.SuccessCount.ShouldBe(2);
        result.FailureCount.ShouldBe(0);
        result.Rows.ShouldAllBe(r => r.Succeeded && r.EmployeeId.HasValue);
    }

    [Fact]
    public async Task HiringInBulk_WithOneRowReferencingAMissingDepartmentCode_FailsOnlyThatRow()
    {
        using HttpClient client = factory.CreateClientFor(
            Permissions.Employee.Create,
            Permissions.Organization.LegalEntityManage,
            Permissions.Organization.Manage,
            Permissions.Organization.DepartmentManage);

        OrgCodes org = await CreateOrgCodesAsync(client);
        string goodCode = $"BLK-{Guid.NewGuid():N}"[..12];
        string badCode = $"BLK-{Guid.NewGuid():N}"[..12];

        var badRow = BulkRow(badCode, "Missing", "Department", org) with { DepartmentCode = $"NOPE-{Guid.NewGuid():N}"[..10] };

        var response = await client.PostAsJsonAsync(
            "/api/v1/employees/bulk",
            new { Rows = new[] { BulkRow(goodCode, "Priya", "Sharma", org), badRow } },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        BulkHireResultResponse result = (await response.Content.ReadFromJsonAsync<BulkHireResultResponse>(
            TestContext.Current.CancellationToken))!;

        result.TotalRows.ShouldBe(2);
        result.SuccessCount.ShouldBe(1);
        result.FailureCount.ShouldBe(1);

        result.Rows[0].Succeeded.ShouldBeTrue();
        result.Rows[1].Succeeded.ShouldBeFalse();
        result.Rows[1].ErrorCode.ShouldBe("NOT_FOUND");
    }

    [Fact]
    public async Task HiringInBulk_WithDuplicateEmployeeCodesAcrossRows_FailsTheSecondOccurrence()
    {
        using HttpClient client = factory.CreateClientFor(
            Permissions.Employee.Create,
            Permissions.Organization.LegalEntityManage,
            Permissions.Organization.Manage,
            Permissions.Organization.DepartmentManage);

        OrgCodes org = await CreateOrgCodesAsync(client);
        string duplicateCode = $"BLK-{Guid.NewGuid():N}"[..12];

        var response = await client.PostAsJsonAsync(
            "/api/v1/employees/bulk",
            new
            {
                Rows = new[]
                {
                    BulkRow(duplicateCode, "Priya", "Sharma", org),
                    BulkRow(duplicateCode, "Priya", "Duplicate", org),
                },
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        BulkHireResultResponse result = (await response.Content.ReadFromJsonAsync<BulkHireResultResponse>(
            TestContext.Current.CancellationToken))!;

        result.SuccessCount.ShouldBe(1);
        result.FailureCount.ShouldBe(1);
        result.Rows[1].ErrorCode.ShouldBe("EMPLOYEE_CODE_IN_USE");
    }

    [Fact]
    public async Task HiringInBulk_WithMoreThanFiveHundredRows_IsRejectedAsUnprocessable()
    {
        using HttpClient client = factory.CreateClientFor(
            Permissions.Employee.Create,
            Permissions.Organization.LegalEntityManage,
            Permissions.Organization.Manage,
            Permissions.Organization.DepartmentManage);

        OrgCodes org = await CreateOrgCodesAsync(client);
        var rows = Enumerable.Range(0, 501)
            .Select(i => BulkRow($"OVR-{i:D5}", "Too", "Many", org))
            .ToArray();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/employees/bulk",
            new { Rows = rows },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    private static BulkRowDto BulkRow(string employeeCode, string firstName, string lastName, OrgCodes org) => new(
        employeeCode, firstName, lastName, new DateOnly(2026, 2, 1),
        org.LegalEntityCode, org.BranchCode, org.DepartmentCode, org.DesignationCode, EmploymentType: 1);

    private sealed record BulkRowDto(
        string EmployeeCode, string FirstName, string LastName, DateOnly DateOfJoining,
        string LegalEntityCode, string BranchCode, string DepartmentCode, string DesignationCode, int EmploymentType);

    private sealed record OrgCodes(string LegalEntityCode, string BranchCode, string DepartmentCode, string DesignationCode);

    private static async Task<OrgCodes> CreateOrgCodesAsync(HttpClient client)
    {
        string legalEntityCode = $"LE-{Guid.NewGuid():N}"[..12];
        HttpResponseMessage legalEntityResponse = await client.PostAsJsonAsync(
            "/api/v1/legal-entities",
            new { Code = legalEntityCode, Name = "Acme Corp", CountryCode = "IN", Currency = "INR" },
            TestContext.Current.CancellationToken);
        LegalEntityResponse legalEntity = (await legalEntityResponse.Content.ReadFromJsonAsync<LegalEntityResponse>(
            TestContext.Current.CancellationToken))!;

        string branchCode = $"BR-{Guid.NewGuid():N}"[..12];
        HttpResponseMessage branchResponse = await client.PostAsJsonAsync(
            "/api/v1/branches",
            new { Code = branchCode, Name = "Head Office", LegalEntityId = legalEntity.Id, CountryCode = "IN", TimeZone = "Asia/Kolkata" },
            TestContext.Current.CancellationToken);
        _ = (await branchResponse.Content.ReadFromJsonAsync<BranchResponse>(TestContext.Current.CancellationToken))!;

        string departmentCode = $"DEPT-{Guid.NewGuid():N}"[..12];
        HttpResponseMessage departmentResponse = await client.PostAsJsonAsync(
            "/api/v1/departments",
            new { Code = departmentCode, Name = "Engineering", ParentDepartmentId = (Guid?)null },
            TestContext.Current.CancellationToken);
        _ = (await departmentResponse.Content.ReadFromJsonAsync<DepartmentResponse>(TestContext.Current.CancellationToken))!;

        string designationCode = $"DSG-{Guid.NewGuid():N}"[..12];
        HttpResponseMessage designationResponse = await client.PostAsJsonAsync(
            "/api/v1/designations",
            new { Code = designationCode, Title = "Engineer", JobFamily = (string?)null, JobLevel = (int?)null },
            TestContext.Current.CancellationToken);
        _ = (await designationResponse.Content.ReadFromJsonAsync<DesignationResponse>(TestContext.Current.CancellationToken))!;

        return new OrgCodes(legalEntityCode, branchCode, departmentCode, designationCode);
    }
}
