using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;

namespace Gggs.Api.IntegrationTests;

/// <summary>API keys (Studio): a key acts as a machine account holding its roles, and nothing more.</summary>
[Collection(nameof(ApiCollection))]
public sealed class ApiKeyTests(PeopleOsApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AKey_CanDoWhatItsRolesAllow_AndNothingElse()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid role = await RoleAsync(admin, ("employee.profile.view", "Tenant"));
        (string value, Guid id) = await CreateKeyAsync(admin, "Directory sync", role);
        value.ShouldStartWith($"pk_{PeopleOsApiFactory.SeededTenantCode}_");

        using HttpClient machine = KeyClient(value);
        (await machine.GetAsync("/api/v1/employees", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await machine.GetAsync("/api/v1/roles", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden, "the key's role does not include role administration");

        JsonElement listed = (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/api-keys", Ct))!.Single(key => key.GetProperty("id").GetGuid() == id);
        listed.GetProperty("status").GetString().ShouldBe("Active");
        listed.GetProperty("roles").EnumerateArray().Single().GetProperty("id").GetGuid().ShouldBe(role);
        listed.TryGetProperty("lastUsedAt", out _).ShouldBeTrue("using the key records it");

        // Its machine account is not listed among people.
        string users = await admin.GetStringAsync("/api/v1/users?pageSize=200", Ct);
        users.ShouldNotContain("API key: Directory sync");
    }

    [Fact]
    public async Task ARevokedKey_AndAKeyThatIsNotOne_AreBothRefused()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid role = await RoleAsync(admin, ("employee.profile.view", "Tenant"));
        (string value, Guid id) = await CreateKeyAsync(admin, "Short-lived", role);

        (await admin.PostAsync($"/api/v1/api-keys/{id}/revoke", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using (HttpClient revoked = KeyClient(value))
        {
            (await revoked.GetAsync("/api/v1/employees", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        using HttpClient forged = KeyClient(value[..^4] + "0000");
        (await forged.GetAsync("/api/v1/employees", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using HttpClient junk = KeyClient("not-a-key");
        (await junk.GetAsync("/api/v1/employees", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AKey_NeedsARole_AndCannotNameAnotherOrganisation()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        HttpResponseMessage noRole = await admin.PostAsJsonAsync("/api/v1/api-keys", new { Name = "Nothing", RoleIds = Array.Empty<Guid>() }, Ct);
        (await CoreHrApi.ErrorCodeAsync(noRole)).ShouldBe("API_KEY_ROLES");

        Guid role = await RoleAsync(admin, ("employee.profile.view", "Tenant"));
        (string value, _) = await CreateKeyAsync(admin, "Moved", role);

        // The same prefix and secret under another organisation's code finds nothing there.
        string elsewhere = value.Replace($"pk_{PeopleOsApiFactory.SeededTenantCode}_", $"pk_{PeopleOsApiFactory.OtherTenantCode}_", StringComparison.Ordinal);
        using HttpClient crossed = KeyClient(elsewhere);
        (await crossed.GetAsync("/api/v1/employees", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private HttpClient KeyClient(string key)
    {
        HttpClient client = factory.CreateRawClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", key);
        return client;
    }

    private static async Task<(string Value, Guid Id)> CreateKeyAsync(HttpClient admin, string name, Guid role)
    {
        HttpResponseMessage created = await admin.PostAsJsonAsync("/api/v1/api-keys", new { Name = name, RoleIds = new[] { role }, ExpiresInDays = 30 }, Ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        JsonElement body = await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return (body.GetProperty("value").GetString()!, body.GetProperty("key").GetProperty("id").GetGuid());
    }

    private static async Task<Guid> RoleAsync(HttpClient admin, params (string Code, string Scope)[] permissions)
    {
        Guid role = await CoreHrApi.CreateAsync(admin, "/api/v1/roles", new { Name = $"Key role {Guid.NewGuid():N}", Description = "API key test" });
        HttpResponseMessage saved = await admin.PutAsJsonAsync($"/api/v1/roles/{role}/permissions", new
        {
            Permissions = permissions.Select(permission => new { PermissionCode = permission.Code, permission.Scope }),
        }, Ct);
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(Ct));
        return role;
    }
}
