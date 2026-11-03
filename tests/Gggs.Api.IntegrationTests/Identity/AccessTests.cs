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

/// <summary>The access screens: who holds which role, what each person may do and why, and the role grid's save.</summary>
[Collection(nameof(ApiCollection))]
public sealed class AccessTests(PeopleOsApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AUsersAccess_ShowsTheirRoles_AndEachPermissionWithTheRolesItComesThrough()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid teamLead = await CreateRoleAsync(admin, ("leave.request.view", "DirectReports"));
        Guid hrManager = await CreateRoleAsync(admin, ("leave.request.view", "Branch"), ("employee.profile.view", "Branch"));
        (Guid user, HttpClient _) = await UserWithRolesAsync(admin, teamLead, hrManager);

        JsonElement access = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/users/{user}/access", Ct);
        // Besides the self-service role the account was created with.
        Guid[] held = [.. access.GetProperty("user").GetProperty("roles").EnumerateArray().Select(role => role.GetProperty("roleId").GetGuid())];
        held.ShouldContain(teamLead);
        held.ShouldContain(hrManager);

        JsonElement leave = access.GetProperty("permissions").EnumerateArray().Single(permission => permission.GetProperty("code").GetString() == "leave.request.view");
        leave.GetProperty("scope").GetString().ShouldBe("Branch", "the wider reach wins, as it does when requests are authorized");
        leave.GetProperty("via").EnumerateArray().Count().ShouldBe(2);

        JsonElement listed = (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/access/users", Ct))!.Single(row => row.GetProperty("id").GetGuid() == user);
        listed.GetProperty("permissionCodes").EnumerateArray().Select(code => code.GetString()).ShouldContain("employee.profile.view");

        JsonElement[] members = (await admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/roles/{hrManager}/members", Ct))!;
        members.ShouldHaveSingleItem().GetProperty("userId").GetGuid().ShouldBe(user);
    }

    [Fact]
    public async Task SavingARolesGrid_ReplacesItsPermissions_AndTheHolderHasThemAtOnce()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid role = await CreateRoleAsync(admin, ("leave.request.view", "DirectReports"));
        (Guid _, HttpClient holder) = await UserWithRolesAsync(admin, role);
        using (holder)
        {
            (await holder.GetAsync("/api/v1/roles", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

            HttpResponseMessage saved = await admin.PutAsJsonAsync($"/api/v1/roles/{role}/permissions", new
            {
                Permissions = new[] { new { PermissionCode = "admin.role.view", Scope = "Tenant" } },
            }, Ct);
            saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(Ct));
            (await saved.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("permissions").EnumerateArray()
                .Select(permission => permission.GetProperty("permissionCode").GetString()).ShouldBe(["admin.role.view"]);

            // No sign-out and no cache expiry: the role's holders are refreshed with the save.
            (await holder.GetAsync("/api/v1/roles", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task AGridNamingAPermissionThePlatformDoesNotDefine_SavesNothing()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid role = await CreateRoleAsync(admin, ("leave.request.view", "DirectReports"));

        HttpResponseMessage saved = await admin.PutAsJsonAsync($"/api/v1/roles/{role}/permissions", new
        {
            Permissions = new[] { new { PermissionCode = "admin.role.view", Scope = "Tenant" }, new { PermissionCode = "made.up.permission", Scope = "Tenant" } },
        }, Ct);

        saved.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CoreHrApi.ErrorCodeAsync(saved)).ShouldBe("PERMISSION_UNKNOWN");
        JsonElement unchanged = (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/roles", Ct))!.Single(candidate => candidate.GetProperty("id").GetGuid() == role);
        unchanged.GetProperty("permissions").EnumerateArray().Select(permission => permission.GetProperty("permissionCode").GetString()).ShouldBe(["leave.request.view"]);
    }

    private static async Task<Guid> CreateRoleAsync(HttpClient admin, params (string Code, string Scope)[] permissions)
    {
        Guid role = await CoreHrApi.CreateAsync(admin, "/api/v1/roles", new { Name = $"Access {Guid.NewGuid():N}", Description = "Access test" });
        HttpResponseMessage saved = await admin.PutAsJsonAsync($"/api/v1/roles/{role}/permissions", new
        {
            Permissions = permissions.Select(permission => new { PermissionCode = permission.Code, permission.Scope }),
        }, Ct);
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(Ct));
        return role;
    }

    /// <summary>A signed-in user holding exactly these roles besides a self-service one.</summary>
    private async Task<(Guid User, HttpClient Client)> UserWithRolesAsync(HttpClient admin, params Guid[] roles)
    {
        string email = Email("access");
        HttpClient client = await factory.CreateScopedUserClientAsync(email, null, (Permissions.Self.ProfileView, DataScope.Own));

        Guid user;
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenant(PeopleOsApiFactory.SeededTenantId);
            user = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<User>().Where(candidate => candidate.Email == email).Select(candidate => candidate.Id).SingleAsync(Ct);
        }

        Guid[] current = (await admin.GetFromJsonAsync<Guid[]>($"/api/v1/users/{user}/roles", Ct))!;
        (await admin.PutAsJsonAsync($"/api/v1/users/{user}/roles", new { RoleIds = current.Concat(roles) }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        return (user, client);
    }
}
