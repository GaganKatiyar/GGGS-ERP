using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Gggs.CoreHr.Api.Endpoints;
using Gggs.CoreHr.Domain.Assets;
using Shouldly;

namespace Gggs.CoreHr.IntegrationTests;

/// <summary><c>/api/v1/assets/categories*</c> and <c>/api/v1/assets*</c> over HTTP: registering a
/// category and an asset, assigning it to a real hired employee, confirming it cannot be assigned
/// again while still out, returning it, and the employee seeing it in their own "my assets" list.
/// Architecture.md §5.1's Asset module, kept inside Core HR rather than its own service.</summary>
[Collection(nameof(CoreHrApiCollection))]
public sealed class AssetEndpointTests(CoreHrApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = BuildJsonOptions();

    private static readonly string[] AdminPermissions =
    [
        Permissions.Asset.CategoryManage,
        Permissions.Asset.View,
        Permissions.Asset.Manage,
        Permissions.Organization.Manage,
        Permissions.Organization.LegalEntityManage,
        Permissions.Organization.DepartmentManage,
        Permissions.Employee.Create,
    ];

    private static JsonSerializerOptions BuildJsonOptions()
    {
        var options = new JsonSerializerOptions();
        Gggs.BuildingBlocks.Web.JsonConfiguration.Apply(options);
        return options;
    }

    [Fact]
    public async Task RegisteringAnAsset_WithoutThePermission_IsForbidden()
    {
        using HttpClient client = factory.CreateClientFor();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/assets",
            new CreateAssetBody(ShortTag(), Guid.NewGuid(), "Dell", "Latitude 7420", null),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AssigningAndReturningAnAsset_MovesItThroughInStockAndAssigned()
    {
        using HttpClient client = factory.CreateClientFor(AdminPermissions);

        Guid categoryId = await CreateCategoryAsync(client);
        Guid assetId = await CreateAssetAsync(client, categoryId);
        Guid employeeId = await HireEmployeeAsync(client);

        HttpResponseMessage assignResponse = await client.PostAsJsonAsync(
            $"/api/v1/assets/{assetId}/assign",
            new AssignAssetBody(employeeId),
            TestContext.Current.CancellationToken);
        assignResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        AssetAssignmentResponse? assignment = await assignResponse.Content.ReadFromJsonAsync<AssetAssignmentResponse>(JsonOptions, TestContext.Current.CancellationToken);
        assignment.ShouldNotBeNull();
        assignment.ReturnedDate.ShouldBeNull();

        HttpResponseMessage assetResponse = await client.GetAsync($"/api/v1/assets/{assetId}", TestContext.Current.CancellationToken);
        AssetResponse? asset = await assetResponse.Content.ReadFromJsonAsync<AssetResponse>(JsonOptions, TestContext.Current.CancellationToken);
        asset.ShouldNotBeNull();
        asset.Status.ShouldBe(AssetStatus.Assigned);

        HttpResponseMessage returnResponse = await client.PostAsync(
            $"/api/v1/assets/assignments/{assignment.Id}/return", content: null, TestContext.Current.CancellationToken);
        returnResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        AssetAssignmentResponse? returned = await returnResponse.Content.ReadFromJsonAsync<AssetAssignmentResponse>(JsonOptions, TestContext.Current.CancellationToken);
        returned.ShouldNotBeNull();
        returned.ReturnedDate.ShouldNotBeNull();

        HttpResponseMessage afterReturnResponse = await client.GetAsync($"/api/v1/assets/{assetId}", TestContext.Current.CancellationToken);
        AssetResponse? afterReturn = await afterReturnResponse.Content.ReadFromJsonAsync<AssetResponse>(JsonOptions, TestContext.Current.CancellationToken);
        afterReturn.ShouldNotBeNull();
        afterReturn.Status.ShouldBe(AssetStatus.InStock);
    }

    [Fact]
    public async Task AssigningAnAlreadyAssignedAsset_IsRejected()
    {
        using HttpClient client = factory.CreateClientFor(AdminPermissions);

        Guid categoryId = await CreateCategoryAsync(client);
        Guid assetId = await CreateAssetAsync(client, categoryId);
        Guid firstEmployeeId = await HireEmployeeAsync(client);
        Guid secondEmployeeId = await HireEmployeeAsync(client);

        await client.PostAsJsonAsync(
            $"/api/v1/assets/{assetId}/assign", new AssignAssetBody(firstEmployeeId), TestContext.Current.CancellationToken);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/assets/{assetId}/assign", new AssignAssetBody(secondEmployeeId), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task AnEmployee_CanSeeTheirOwnAssignedAssets()
    {
        using HttpClient adminClient = factory.CreateClientFor(AdminPermissions);

        Guid categoryId = await CreateCategoryAsync(adminClient);
        Guid assetId = await CreateAssetAsync(adminClient, categoryId);
        Guid employeeId = await HireEmployeeAsync(adminClient);

        await adminClient.PostAsJsonAsync(
            $"/api/v1/assets/{assetId}/assign", new AssignAssetBody(employeeId), TestContext.Current.CancellationToken);

        using HttpClient ownerClient = factory.CreateClientForEmployee(employeeId, Permissions.Self.AssetView);
        HttpResponseMessage response = await ownerClient.GetAsync("/api/v1/assets/assignments/me", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        List<AssetAssignmentResponse>? items = await response.Content.ReadFromJsonAsync<List<AssetAssignmentResponse>>(JsonOptions, TestContext.Current.CancellationToken);
        items.ShouldNotBeNull();
        items.ShouldContain(x => x.AssetId == assetId);
    }

    [Fact]
    public async Task RetiringAnAssignedAsset_IsRejected()
    {
        using HttpClient client = factory.CreateClientFor(AdminPermissions);

        Guid categoryId = await CreateCategoryAsync(client);
        Guid assetId = await CreateAssetAsync(client, categoryId);
        Guid employeeId = await HireEmployeeAsync(client);

        await client.PostAsJsonAsync(
            $"/api/v1/assets/{assetId}/assign", new AssignAssetBody(employeeId), TestContext.Current.CancellationToken);

        HttpResponseMessage response = await client.PostAsync(
            $"/api/v1/assets/{assetId}/retire", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    private static async Task<Guid> CreateCategoryAsync(HttpClient client)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/assets/categories",
            new CreateAssetCategoryBody(ShortCode(), "Laptop"),
            TestContext.Current.CancellationToken);

        AssetCategoryResponse? body = await response.Content.ReadFromJsonAsync<AssetCategoryResponse>(JsonOptions, TestContext.Current.CancellationToken);
        return body!.Id;
    }

    private static async Task<Guid> CreateAssetAsync(HttpClient client, Guid categoryId)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/assets",
            new CreateAssetBody(ShortTag(), categoryId, "Dell", "Latitude 7420", $"SN{Guid.NewGuid():N}"[..12]),
            TestContext.Current.CancellationToken);

        AssetResponse? body = await response.Content.ReadFromJsonAsync<AssetResponse>(JsonOptions, TestContext.Current.CancellationToken);
        return body!.Id;
    }

    /// <summary>Hires a minimal real employee through the real org structure, enough for
    /// AssetAssignmentService's own employee-exists/not-exited check to accept.</summary>
    private static async Task<Guid> HireEmployeeAsync(HttpClient client)
    {
        HttpResponseMessage legalEntityResponse = await client.PostAsJsonAsync(
            "/api/v1/legal-entities",
            new { Code = $"LE-{Guid.NewGuid():N}"[..12], Name = "Acme Corp", CountryCode = "IN", Currency = "INR" },
            TestContext.Current.CancellationToken);
        string legalEntityBody = await legalEntityResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        legalEntityResponse.StatusCode.ShouldBe(HttpStatusCode.Created, legalEntityBody);
        LegalEntityResponse legalEntity = JsonSerializer.Deserialize<LegalEntityResponse>(legalEntityBody, JsonOptions)!;

        HttpResponseMessage branchResponse = await client.PostAsJsonAsync(
            "/api/v1/branches",
            new { Code = $"BR-{Guid.NewGuid():N}"[..12], Name = "Head Office", LegalEntityId = legalEntity.Id, CountryCode = "IN", TimeZone = "Asia/Kolkata" },
            TestContext.Current.CancellationToken);
        BranchResponse branch = (await branchResponse.Content.ReadFromJsonAsync<BranchResponse>(TestContext.Current.CancellationToken))!;

        HttpResponseMessage departmentResponse = await client.PostAsJsonAsync(
            "/api/v1/departments",
            new { Code = $"DEPT-{Guid.NewGuid():N}"[..12], Name = "Engineering", ParentDepartmentId = (Guid?)null },
            TestContext.Current.CancellationToken);
        DepartmentResponse department = (await departmentResponse.Content.ReadFromJsonAsync<DepartmentResponse>(TestContext.Current.CancellationToken))!;

        HttpResponseMessage designationResponse = await client.PostAsJsonAsync(
            "/api/v1/designations",
            new { Code = $"DSG-{Guid.NewGuid():N}"[..12], Title = "Engineer", JobFamily = (string?)null, JobLevel = (int?)null },
            TestContext.Current.CancellationToken);
        DesignationResponse designation = (await designationResponse.Content.ReadFromJsonAsync<DesignationResponse>(TestContext.Current.CancellationToken))!;

        HttpResponseMessage employeeResponse = await client.PostAsJsonAsync(
            "/api/v1/employees",
            new
            {
                EmployeeCode = $"EMP-{Guid.NewGuid():N}"[..12],
                FirstName = "Asset",
                LastName = "Holder",
                DateOfJoining = new DateOnly(2026, 1, 15),
                Assignment = new
                {
                    LegalEntityId = legalEntity.Id,
                    BranchId = branch.Id,
                    DepartmentId = department.Id,
                    DesignationId = designation.Id,
                    EmploymentType = 1,
                },
            },
            TestContext.Current.CancellationToken);

        string body = await employeeResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        employeeResponse.StatusCode.ShouldBe(HttpStatusCode.Created, body);

        EmployeeResponse employee = JsonSerializer.Deserialize<EmployeeResponse>(body, JsonOptions)!;
        return employee.Id;
    }

    private static string ShortCode() => $"C{Guid.NewGuid():N}"[..18];

    private static string ShortTag() => $"AST-{Guid.NewGuid():N}"[..14];
}
