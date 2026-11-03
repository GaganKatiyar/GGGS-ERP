using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using static Gggs.Api.IntegrationTests.PlatformServicesApi;

namespace Gggs.Api.IntegrationTests;

/// <summary>
/// Roles that follow the org chart (Role.AutoAssign). Each test makes its own automatic role and
/// switches it off afterwards, so the tenant shared by the run is left as it was found.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class RoleAutomationTests(PeopleOsApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SomeoneWhoGainsAReport_BecomesAManager_AndStopsBeingOneWhenTheReportMovesAway()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        await using AutomaticRole managers = await AutomaticRole.CreateAsync(admin, "PeopleManagers");
        Guid department = await CoreHrApi.DepartmentAsync(admin);
        Guid manager = await CoreHrApi.HireAsync(admin, department);
        Guid user = await LinkedUserAsync(manager);

        Guid report = await CoreHrApi.HireAsync(admin, department, managerId: manager);
        await factory.DeliverEventsAsync();

        JsonElement automatic = (await AutomaticRolesAsync(admin, user, managers.Id)).ShouldHaveSingleItem();
        automatic.TryGetProperty("departmentId", out JsonElement anchor).ShouldBeFalse($"a people-manager grant is not anchored, but was to {anchor}");
        (await ManualRolesAsync(admin, user)).ShouldNotContain(managers.Id, "the editor shows only roles given by hand");

        await MoveAwayAsync(admin, report, department);
        await factory.DeliverEventsAsync();

        (await AutomaticRolesAsync(admin, user, managers.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task ARoleGivenByHand_IsTheAdministrators_AndOutlastsTheOrgChart()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        await using AutomaticRole managers = await AutomaticRole.CreateAsync(admin, "PeopleManagers");
        Guid department = await CoreHrApi.DepartmentAsync(admin);
        Guid manager = await CoreHrApi.HireAsync(admin, department);
        Guid user = await LinkedUserAsync(manager);

        Guid report = await CoreHrApi.HireAsync(admin, department, managerId: manager);
        await factory.DeliverEventsAsync();
        (await AutomaticRolesAsync(admin, user, managers.Id)).ShouldHaveSingleItem();

        // Ticking the role it already holds automatically makes that grant a manual one, not a duplicate.
        List<Guid> roles = [.. await ManualRolesAsync(admin, user), managers.Id];
        (await admin.PutAsJsonAsync($"/api/v1/users/{user}/roles", new { RoleIds = roles }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await AutomaticRolesAsync(admin, user, managers.Id)).ShouldBeEmpty();
        (await ManualRolesAsync(admin, user)).ShouldContain(managers.Id);

        await MoveAwayAsync(admin, report, department);
        await factory.DeliverEventsAsync();

        (await ManualRolesAsync(admin, user)).ShouldContain(managers.Id);
    }

    [Fact]
    public async Task ADepartmentsHead_HoldsDepartmentHead_ForThatDepartment_UntilTheRoleIsSwitchedOff()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        await using AutomaticRole heads = await AutomaticRole.CreateAsync(admin, "DepartmentHeads");
        Guid home = await CoreHrApi.DepartmentAsync(admin);
        Guid head = await CoreHrApi.HireAsync(admin, home);
        Guid user = await LinkedUserAsync(head);

        Guid headed = await CoreHrApi.CreateAsync(admin, "/api/v1/organizations/departments", new { Code = CoreHrApi.Unique("DEPT"), Name = "Headed " + CoreHrApi.Unique("N"), HeadEmployeeId = head });
        await factory.DeliverEventsAsync();

        JsonElement grant = (await AutomaticRolesAsync(admin, user, heads.Id)).ShouldHaveSingleItem();
        grant.GetProperty("departmentId").GetGuid().ShouldBe(headed);
        grant.GetProperty("departmentName").GetString().ShouldStartWith("Headed ");

        // Switching the role off ends the grants it made, at once.
        (await heads.SetAsync("None")).GetProperty("autoAssign").GetString().ShouldBe("None");
        (await AutomaticRolesAsync(admin, user, heads.Id)).ShouldBeEmpty();

        // And on again: an ended grant is history, so the role can be given a second time.
        await heads.SetAsync("DepartmentHeads");
        (await AutomaticRolesAsync(admin, user, heads.Id)).ShouldHaveSingleItem();
    }

    /// <summary>A role made for one test, assigned automatically, and switched off when the test ends.</summary>
    private sealed class AutomaticRole(HttpClient admin, Guid id) : IAsyncDisposable
    {
        public Guid Id { get; } = id;

        public static async Task<AutomaticRole> CreateAsync(HttpClient admin, string autoAssign)
        {
            HttpResponseMessage created = await admin.PostAsJsonAsync("/api/v1/roles", new { Name = $"Automatic {Guid.NewGuid():N}", Description = "Role automation test" }, Ct);
            created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
            var role = new AutomaticRole(admin, (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid());
            await role.SetAsync(autoAssign);
            return role;
        }

        public async Task<JsonElement> SetAsync(string autoAssign)
        {
            HttpResponseMessage response = await admin.PutAsJsonAsync($"/api/v1/roles/{Id}/auto-assign", new { AutoAssign = autoAssign }, Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
            return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        }

        public async ValueTask DisposeAsync() => await SetAsync("None");
    }

    private static async Task MoveAwayAsync(HttpClient admin, Guid employee, Guid department)
    {
        HttpResponseMessage moved = await admin.PostAsJsonAsync($"/api/v1/employees/{employee}/assignment-changes", new
        {
            ChangeType = "ManagerChange",
            EffectiveFrom = TimeApi.Iso(DateOnly.FromDateTime(DateTime.UtcNow)),
            Reason = "New team",
            Assignment = new { DepartmentId = department, EmploymentTypeCode = "FULL_TIME", WorkModeCode = "OFFICE", Designation = "Engineer" },
        }, Ct);
        moved.StatusCode.ShouldBe(HttpStatusCode.Created, await moved.Content.ReadAsStringAsync(Ct));
    }

    /// <summary>The user's automatic grants of one role - other automatic roles in the shared tenant are not this test's.</summary>
    private static async Task<JsonElement[]> AutomaticRolesAsync(HttpClient admin, Guid user, Guid role)
        => [.. (await admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/users/{user}/automatic-roles", Ct))!.Where(grant => grant.GetProperty("roleId").GetGuid() == role)];

    private static async Task<Guid[]> ManualRolesAsync(HttpClient admin, Guid user)
        => (await admin.GetFromJsonAsync<Guid[]>($"/api/v1/users/{user}/roles", Ct))!;

    /// <summary>A signed-in account for the employee, and its user id.</summary>
    private async Task<Guid> LinkedUserAsync(Guid employee)
    {
        string email = Email("linked");
        using HttpClient account = await factory.CreateScopedUserClientAsync(email, employee, (Permissions.Self.ProfileView, DataScope.Own));

        using IServiceScope scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenant(PeopleOsApiFactory.SeededTenantId);
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<User>()
            .Where(user => user.Email == email)
            .Select(user => user.Id)
            .SingleAsync(Ct);
    }
}
