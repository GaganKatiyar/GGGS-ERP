using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Shouldly;

namespace Gggs.Api.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class MasterDataAndOrganizationTests(PeopleOsApiFactory factory)
{
    [Fact]
    public async Task ANewTenant_HasTheLookupListsItsFormsNeed()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();

        JsonElement[] states = (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/master-data/state?parent=IN", TestContext.Current.CancellationToken))!;

        states.Length.ShouldBe(36, "28 states and 8 union territories");
        states.ShouldContain(state => state.GetProperty("code").GetString() == "IN-KA");
    }

    [Fact]
    public async Task ATenant_CanExtendItsOwnListsButNotTheStandards()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();

        (await admin.PostAsJsonAsync("/api/v1/master-data/work-mode", new { Code = "FOUR_DAY", Label = "Four-day week" }, TestContext.Current.CancellationToken))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        HttpResponseMessage refused = await admin.PostAsJsonAsync("/api/v1/master-data/country", new { Code = "XX", Label = "Nowhere" }, TestContext.Current.CancellationToken);
        refused.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CoreHrApi.ErrorCodeAsync(refused)).ShouldBe("LOOKUP_LIST_NOT_EXTENSIBLE");
    }

    [Fact]
    public async Task ADeactivatedValue_IsNoLongerOfferedOrAccepted()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid id = await CoreHrApi.CreateAsync(admin, "/api/v1/master-data/job-family", new { Code = CoreHrApi.Unique("FAM"), Label = "Short-lived" });

        JsonElement created = (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/master-data/job-family", TestContext.Current.CancellationToken))!
            .Single(value => value.GetProperty("id").GetGuid() == id);
        string code = created.GetProperty("code").GetString()!;

        (await admin.PostAsync($"/api/v1/master-data/job-family/{id}/deactivate", null, TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);

        JsonElement[] active = (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/master-data/job-family", TestContext.Current.CancellationToken))!;
        active.ShouldNotContain(value => value.GetProperty("id").GetGuid() == id);

        HttpResponseMessage job = await admin.PostAsJsonAsync("/api/v1/organizations/roles", new { Code = CoreHrApi.Unique("JOB"), Name = "Uses a retired family", JobFamilyCode = code }, TestContext.Current.CancellationToken);
        job.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CoreHrApi.ErrorCodeAsync(job)).ShouldBe("LOOKUP_CODE_INVALID");
    }

    [Fact]
    public async Task ACodeIsUniquePerKind_AndTheErrorSaysSo()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        string code = CoreHrApi.Unique("LOC");

        await CoreHrApi.CreateAsync(admin, "/api/v1/organizations/locations", new { Code = code, Name = "First" });
        HttpResponseMessage duplicate = await admin.PostAsJsonAsync("/api/v1/organizations/locations", new { Code = code.ToLowerInvariant(), Name = "Second" }, TestContext.Current.CancellationToken);

        duplicate.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CoreHrApi.ErrorCodeAsync(duplicate)).ShouldBe("ORG_CODE_IN_USE");
    }

    [Fact]
    public async Task ACodeCanBeChanged_ButNotToOneInUse()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        string taken = CoreHrApi.Unique("LOC");
        await CoreHrApi.CreateAsync(admin, "/api/v1/organizations/locations", new { Code = taken, Name = "Pune" });
        Guid location = await CoreHrApi.CreateAsync(admin, "/api/v1/organizations/locations", new { Code = CoreHrApi.Unique("LOC"), Name = "Kannauj HQ" });

        string renamed = CoreHrApi.Unique("KNJ");
        HttpResponseMessage changed = await admin.PutAsJsonAsync($"/api/v1/organizations/locations/{location}", new { Code = renamed.ToLowerInvariant(), Name = "Kannauj HQ" }, TestContext.Current.CancellationToken);
        changed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        JsonElement saved = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/locations/{location}", TestContext.Current.CancellationToken);
        saved.GetProperty("code").GetString().ShouldBe(renamed);

        HttpResponseMessage clash = await admin.PutAsJsonAsync($"/api/v1/organizations/locations/{location}", new { Code = taken, Name = "Kannauj HQ" }, TestContext.Current.CancellationToken);
        clash.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CoreHrApi.ErrorCodeAsync(clash)).ShouldBe("ORG_CODE_IN_USE");
    }

    [Fact]
    public async Task ADepartmentCannotBeMovedUnderItsOwnFunction()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid department = await CoreHrApi.DepartmentAsync(admin);
        Guid function = await CoreHrApi.DepartmentAsync(admin, department);

        HttpResponseMessage response = await admin.PutAsJsonAsync($"/api/v1/organizations/departments/{department}", new { Code = "IGNORED", Name = "Parent", ParentDepartmentId = function }, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CoreHrApi.ErrorCodeAsync(response)).ShouldBe("DEPARTMENT_CYCLE");
    }

    [Fact]
    public async Task AFunctionCannotHoldAnotherFunction()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid department = await CoreHrApi.DepartmentAsync(admin);
        Guid function = await CoreHrApi.DepartmentAsync(admin, department);

        HttpResponseMessage response = await admin.PostAsJsonAsync("/api/v1/organizations/departments", new { Code = CoreHrApi.Unique("FN"), Name = "Too deep", ParentDepartmentId = function }, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CoreHrApi.ErrorCodeAsync(response)).ShouldBe("FUNCTION_IN_FUNCTION");
    }

    [Fact]
    public async Task ADepartmentWithFunctionsCannotBecomeAFunction()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid department = await CoreHrApi.DepartmentAsync(admin);
        await CoreHrApi.DepartmentAsync(admin, department);
        Guid other = await CoreHrApi.DepartmentAsync(admin);

        HttpResponseMessage response = await admin.PutAsJsonAsync($"/api/v1/organizations/departments/{department}", new { Code = "IGNORED", Name = "Moved", ParentDepartmentId = other }, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CoreHrApi.ErrorCodeAsync(response)).ShouldBe("DEPARTMENT_HAS_FUNCTIONS");
    }

    [Fact]
    public async Task AFunction_TakesItsDepartmentsBusinessUnit()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid unit = await CoreHrApi.CreateAsync(admin, "/api/v1/organizations/business-units", new { Code = CoreHrApi.Unique("BU"), Name = "Delivery" });
        Guid otherUnit = await CoreHrApi.CreateAsync(admin, "/api/v1/organizations/business-units", new { Code = CoreHrApi.Unique("BU"), Name = "Corporate" });
        Guid department = await CoreHrApi.CreateAsync(admin, "/api/v1/organizations/departments", new { Code = CoreHrApi.Unique("DEPT"), Name = "Engineering", BusinessUnitId = unit });

        // Whatever business unit the caller names, a function sits in its department's.
        Guid function = await CoreHrApi.CreateAsync(admin, "/api/v1/organizations/departments", new { Code = CoreHrApi.Unique("FN"), Name = "Backend", ParentDepartmentId = department, BusinessUnitId = otherUnit });

        JsonElement saved = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/departments/{function}", TestContext.Current.CancellationToken);
        saved.GetProperty("businessUnitId").GetGuid().ShouldBe(unit);
    }

    [Fact]
    public async Task TheDepartmentTree_NestsChildrenAndCountsPeople()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid parent = await CoreHrApi.DepartmentAsync(admin);
        Guid child = await CoreHrApi.DepartmentAsync(admin, parent);
        await CoreHrApi.HireAsync(admin, child);
        await CoreHrApi.HireAsync(admin, child);

        JsonElement[] tree = (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/organizations/departments/tree", TestContext.Current.CancellationToken))!;
        JsonElement root = tree.Single(node => node.GetProperty("id").GetGuid() == parent);
        JsonElement nested = root.GetProperty("children").EnumerateArray().Single(node => node.GetProperty("id").GetGuid() == child);

        nested.GetProperty("headcount").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task ADepartmentWithPeopleCannotBeClosed()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid department = await CoreHrApi.DepartmentAsync(admin);
        await CoreHrApi.HireAsync(admin, department);

        HttpResponseMessage response = await admin.PostAsJsonAsync($"/api/v1/organizations/departments/{department}/close", new { EffectiveTo = "2030-01-01" }, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CoreHrApi.ErrorCodeAsync(response)).ShouldBe("DEPARTMENT_HAS_EMPLOYEES");
    }

    [Fact]
    public async Task AnotherTenantsStructure_IsInvisible()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid department = await CoreHrApi.DepartmentAsync(admin);

        using HttpClient other = factory.CreateClientFor(PeopleOsApiFactory.OtherTenantId, Permissions.Organization.View, Permissions.Organization.DepartmentManage);

        (await other.GetAsync($"/api/v1/organizations/departments/{department}", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Nor can it be used as a reference from the other tenant.
        HttpResponseMessage child = await other.PostAsJsonAsync("/api/v1/organizations/departments", new { Code = CoreHrApi.Unique("X"), Name = "Child", ParentDepartmentId = department }, TestContext.Current.CancellationToken);
        child.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CoreHrApi.ErrorCodeAsync(child)).ShouldBe("ORG_REFERENCE_INVALID");
    }

    [Fact]
    public async Task ViewingStructure_DoesNotLetYouChangeIt()
    {
        using HttpClient viewer = factory.CreateClientFor(PeopleOsApiFactory.SeededTenantId, Permissions.Organization.View);

        (await viewer.GetAsync("/api/v1/organizations/departments", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await viewer.PostAsJsonAsync("/api/v1/organizations/departments", new { Code = CoreHrApi.Unique("D"), Name = "Nope" }, TestContext.Current.CancellationToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ACompany_StartsWithADefaultCostCenter_ThatStaysUntilAnotherTakesOver()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using HttpClient admin = await factory.CreateAdminClientAsync();
        string code = CoreHrApi.Unique("CO");
        Guid company = await CoreHrApi.CreateAsync(admin, "/api/v1/organizations/companies", new { Code = code, Name = "Sister Co", CountryCode = "IN", CurrencyCode = "INR" });

        JsonElement page = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/cost-centers?search={code}", ct))!;
        JsonElement original = page.GetProperty("items").EnumerateArray().ShouldHaveSingleItem();
        original.GetProperty("isDefault").GetBoolean().ShouldBeTrue();
        original.GetProperty("companyId").GetGuid().ShouldBe(company);
        Guid originalId = original.GetProperty("id").GetGuid();

        HttpResponseMessage refused = await admin.PostAsync($"/api/v1/organizations/cost-centers/{originalId}/deactivate", null, ct);
        refused.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CoreHrApi.ErrorCodeAsync(refused)).ShouldBe("COST_CENTER_DEFAULT");

        // Making another the default hands the flag over.
        Guid other = await CoreHrApi.CreateAsync(admin, "/api/v1/organizations/cost-centers", new { Code = CoreHrApi.Unique("CC"), Name = "Sister Co Pune", CompanyId = company, IsDefault = true });
        JsonElement after = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/cost-centers/{originalId}", ct))!;
        after.GetProperty("isDefault").GetBoolean().ShouldBeFalse();
        (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/cost-centers/{other}", ct)).GetProperty("isDefault").GetBoolean().ShouldBeTrue();
    }
}
