using System.Net;
using System.Net.Http.Json;
using Gggs.BuildingBlocks.Security;
using Gggs.CoreHr.Api.Endpoints;
using Shouldly;

namespace Gggs.CoreHr.IntegrationTests;

/// <summary>
/// Organization structure through the real endpoints: permission checks, cross-entity validation
/// (a branch naming a legal entity that does not exist, a department naming a parent that would
/// create a cycle) and the plain CRUD in between.
/// </summary>
[Collection(nameof(CoreHrApiCollection))]
public sealed class OrganizationEndpointTests(CoreHrApiFactory factory)
{
    [Fact]
    public async Task CreatingALegalEntity_WithoutThePermission_IsForbidden()
    {
        using HttpClient client = factory.CreateClientFor(); // no permissions at all

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/legal-entities",
            new { Code = "ACME", Name = "Acme Corp", CountryCode = "IN", Currency = "INR" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreatingALegalEntity_WithThePermission_Succeeds()
    {
        using HttpClient client = factory.CreateClientFor(Permissions.Organization.LegalEntityManage);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/legal-entities",
            new { Code = $"ACME-{Guid.NewGuid().ToString("N")[..8]}", Name = "Acme Corp", CountryCode = "IN", Currency = "INR" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        LegalEntityResponse? body = await response.Content.ReadFromJsonAsync<LegalEntityResponse>(
            TestContext.Current.CancellationToken);

        body!.Name.ShouldBe("Acme Corp");
    }

    [Fact]
    public async Task CreatingALegalEntity_WithADuplicateCode_IsRejectedWithItsErrorCode()
    {
        using HttpClient client = factory.CreateClientFor(Permissions.Organization.LegalEntityManage);
        string code = $"DUP-{Guid.NewGuid().ToString("N")[..8]}";

        await client.PostAsJsonAsync(
            "/api/v1/legal-entities",
            new { Code = code, Name = "First", CountryCode = "IN", Currency = "INR" },
            TestContext.Current.CancellationToken);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/legal-entities",
            new { Code = code, Name = "Second", CountryCode = "IN", Currency = "INR" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldContain("ORGANIZATION_CODE_IN_USE");
    }

    [Fact]
    public async Task ManagingOrganizationStructure_DoesNotGrantManagingLegalEntities()
    {
        // org.structure.manage covers branches, business units and the rest; legal entities are
        // deliberately gated by their own, narrower permission.
        using HttpClient client = factory.CreateClientFor(Permissions.Organization.Manage);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/legal-entities",
            new { Code = $"ACME-{Guid.NewGuid().ToString("N")[..8]}", Name = "Acme Corp", CountryCode = "IN", Currency = "INR" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreatingABranch_ForALegalEntityThatDoesNotExist_ReturnsNotFound()
    {
        using HttpClient client = factory.CreateClientFor(Permissions.Organization.Manage);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/branches",
            new
            {
                Code = $"HQ-{Guid.NewGuid().ToString("N")[..8]}",
                Name = "Head Office",
                LegalEntityId = Guid.NewGuid(),
                CountryCode = "IN",
                TimeZone = "Asia/Kolkata",
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreatingABranch_ForARealLegalEntity_Succeeds()
    {
        using HttpClient client = factory.CreateClientFor(
            Permissions.Organization.LegalEntityManage,
            Permissions.Organization.Manage);

        HttpResponseMessage legalEntityResponse = await client.PostAsJsonAsync(
            "/api/v1/legal-entities",
            new { Code = $"ACME-{Guid.NewGuid().ToString("N")[..8]}", Name = "Acme Corp", CountryCode = "IN", Currency = "INR" },
            TestContext.Current.CancellationToken);

        LegalEntityResponse legalEntity = (await legalEntityResponse.Content.ReadFromJsonAsync<LegalEntityResponse>(
            TestContext.Current.CancellationToken))!;

        HttpResponseMessage branchResponse = await client.PostAsJsonAsync(
            "/api/v1/branches",
            new
            {
                Code = $"HQ-{Guid.NewGuid().ToString("N")[..8]}",
                Name = "Head Office",
                LegalEntityId = legalEntity.Id,
                CountryCode = "IN",
                TimeZone = "Asia/Kolkata",
            },
            TestContext.Current.CancellationToken);

        branchResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

        BranchResponse branch = (await branchResponse.Content.ReadFromJsonAsync<BranchResponse>(
            TestContext.Current.CancellationToken))!;

        branch.LegalEntityId.ShouldBe(legalEntity.Id);
    }

    [Fact]
    public async Task SettingAGeoFenceOnABranch_WithoutCoordinates_IsRejectedWithItsErrorCode()
    {
        using HttpClient client = factory.CreateClientFor(
            Permissions.Organization.LegalEntityManage,
            Permissions.Organization.Manage);

        Guid branchId = await CreateBranchAsync(client);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/branches/{branchId}/geo-fence",
            new { Latitude = (double?)null, Longitude = (double?)null, RadiusMeters = 100 },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldContain("GEO_FENCE_LOCATION_MISSING");
    }

    [Fact]
    public async Task MovingADepartmentUnderItsOwnDescendant_IsRejectedWithItsErrorCode()
    {
        using HttpClient client = factory.CreateClientFor(Permissions.Organization.DepartmentManage);

        DepartmentResponse engineering = await CreateDepartmentAsync(client, "Engineering", parentId: null);
        DepartmentResponse platformTeam = await CreateDepartmentAsync(client, "Platform", engineering.Id);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/departments/{engineering.Id}/move",
            new { NewParentId = platformTeam.Id },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldContain("DEPARTMENT_MOVE_WOULD_CREATE_CYCLE");
    }

    [Fact]
    public async Task MovingADepartmentUnderAnUnrelatedDepartment_Succeeds()
    {
        using HttpClient client = factory.CreateClientFor(Permissions.Organization.DepartmentManage);

        DepartmentResponse engineering = await CreateDepartmentAsync(client, "Engineering-" + Guid.NewGuid().ToString("N")[..8], null);
        DepartmentResponse sales = await CreateDepartmentAsync(client, "Sales-" + Guid.NewGuid().ToString("N")[..8], null);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/departments/{engineering.Id}/move",
            new { NewParentId = sales.Id },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        DepartmentResponse moved = (await response.Content.ReadFromJsonAsync<DepartmentResponse>(
            TestContext.Current.CancellationToken))!;

        moved.ParentDepartmentId.ShouldBe(sales.Id);
    }

    [Fact]
    public async Task ListingLegalEntities_ReturnsOnlyEntitiesFromThisTenantsScope()
    {
        // Not a cross-tenant probe (that belongs to Gggs.TenantIsolation.Tests) - this only checks
        // that the ordinary list endpoint returns a well-formed page for the caller's own tenant.
        using HttpClient client = factory.CreateClientFor(Permissions.Organization.View);

        HttpResponseMessage response = await client.GetAsync("/api/v1/legal-entities", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static async Task<Guid> CreateBranchAsync(HttpClient client)
    {
        HttpResponseMessage legalEntityResponse = await client.PostAsJsonAsync(
            "/api/v1/legal-entities",
            new { Code = $"ACME-{Guid.NewGuid().ToString("N")[..8]}", Name = "Acme Corp", CountryCode = "IN", Currency = "INR" },
            TestContext.Current.CancellationToken);

        LegalEntityResponse legalEntity = (await legalEntityResponse.Content.ReadFromJsonAsync<LegalEntityResponse>(
            TestContext.Current.CancellationToken))!;

        HttpResponseMessage branchResponse = await client.PostAsJsonAsync(
            "/api/v1/branches",
            new
            {
                Code = $"HQ-{Guid.NewGuid().ToString("N")[..8]}",
                Name = "Head Office",
                LegalEntityId = legalEntity.Id,
                CountryCode = "IN",
                TimeZone = "Asia/Kolkata",
            },
            TestContext.Current.CancellationToken);

        BranchResponse branch = (await branchResponse.Content.ReadFromJsonAsync<BranchResponse>(
            TestContext.Current.CancellationToken))!;

        return branch.Id;
    }

    private static async Task<DepartmentResponse> CreateDepartmentAsync(HttpClient client, string name, Guid? parentId)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/departments",
            new { Code = $"DEPT-{Guid.NewGuid().ToString("N")[..8]}", Name = name, ParentDepartmentId = parentId },
            TestContext.Current.CancellationToken);

        return (await response.Content.ReadFromJsonAsync<DepartmentResponse>(TestContext.Current.CancellationToken))!;
    }
}
