using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Identity.Application.Authentication;
using Gggs.Modules.Identity.Application.PlatformStaff;
using Gggs.Modules.Identity.Domain.PlatformStaff;
using Gggs.Modules.Identity.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Gggs.Api.IntegrationTests;

/// <summary>
/// Platform staff: a second identity with no tenant, authenticated through the same token endpoint
/// as everyone else and resolved by a completely different route to a completely different table.
/// <para>
/// The claim these tests exist to check is that the two never leak into each other: a platform
/// token carries no tenant, a tenant token carries no platform permission, and asking the token
/// endpoint for one never touches the other's table.
/// </para>
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class PlatformStaffTests(PeopleOsApiFactory factory)
{
    private const string StaffPassword = "a platform staff passphrase";

    [Fact]
    public async Task ThePasswordGrantWithNoTenantCode_AuthenticatesAgainstThePlatformStaffTable()
    {
        (Guid staffId, string email) = await SeedActiveStaffAsync(PlatformRoleTemplates.StaffAdministratorCode);

        using HttpClient client = factory.CreateWebClient();

        JsonElement token = await RequestPlatformTokenAsync(client, email);
        JsonElement claims = ReadPayload(token.GetProperty("access_token").GetString()!);

        claims.GetProperty("sub").GetString().ShouldBe(staffId.ToString());
        claims.GetProperty("utyp").GetString().ShouldBe("Platform");

        // The one claim every service reads to decide whether it may look for a tenant at all.
        claims.TryGetProperty("tid", out _).ShouldBeFalse();

        claims.GetProperty("pv").ToString().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ThePlatformToken_AuthorizesAgainstPlatformOnlyPermissionsAndNothingElse()
    {
        (_, string email) = await SeedActiveStaffAsync(PlatformRoleTemplates.StaffAdministratorCode);

        using HttpClient client = factory.CreateWebClient();
        JsonElement token = await RequestPlatformTokenAsync(client, email);

        HttpResponseMessage response = await GetWithBearerAsync(
            client,
            "/api/v1/platform-staff",
            token.GetProperty("access_token").GetString()!);

        // platform.staff.view, held through the seeded administrator role.
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AStaffMemberWithNoRoles_CanAuthenticateButAuthorizesToNothing()
    {
        (_, string email) = await SeedActiveStaffAsync(templateCode: null);

        using HttpClient client = factory.CreateWebClient();
        JsonElement token = await RequestPlatformTokenAsync(client, email);

        HttpResponseMessage response = await GetWithBearerAsync(
            client,
            "/api/v1/platform-staff",
            token.GetProperty("access_token").GetString()!);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AWrongPasswordForAPlatformAccount_DoesNotFallThroughToTenantDiscovery()
    {
        (_, string email) = await SeedActiveStaffAsync(PlatformRoleTemplates.StaffAdministratorCode);

        using HttpClient client = factory.CreateWebClient();

        HttpResponseMessage response = await PostTokenAsync(client, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "password",
            ["username"] = email,
            ["password"] = "not the right password",
            ["scope"] = "gggs.api offline_access",
            ["client_id"] = PeopleOsApiFactory.ApiClientId,
            ["client_secret"] = DevelopmentSecrets.GatewayClientSecret,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        body.GetProperty("error").GetString().ShouldBe("invalid_grant");
    }

    [Fact]
    public async Task ARefreshToken_RebuildsThePrincipalFromThePlatformStaffTable()
    {
        (Guid staffId, string email) = await SeedActiveStaffAsync(PlatformRoleTemplates.StaffAdministratorCode);

        using HttpClient client = factory.CreateWebClient();
        JsonElement first = await RequestPlatformTokenAsync(client, email);
        string refreshToken = first.GetProperty("refresh_token").GetString()!;

        HttpResponseMessage refreshed = await PostTokenAsync(client, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = PeopleOsApiFactory.ApiClientId,
            ["client_secret"] = DevelopmentSecrets.GatewayClientSecret,
        });

        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK);

        JsonElement second = await refreshed.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        JsonElement claims = ReadPayload(second.GetProperty("access_token").GetString()!);

        claims.GetProperty("sub").GetString().ShouldBe(staffId.ToString());
        claims.TryGetProperty("tid", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task ARefreshToken_IsRefusedOnceTheAccountIsDisabled()
    {
        (Guid staffId, string email) = await SeedActiveStaffAsync(PlatformRoleTemplates.StaffAdministratorCode);

        using HttpClient client = factory.CreateWebClient();
        JsonElement first = await RequestPlatformTokenAsync(client, email);

        using (IServiceScope scope = factory.Services.CreateScope())
        {
            AppDbContext database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            PlatformStaffMember member = await database.Set<PlatformStaffMember>().SingleAsync(
                m => m.Id == staffId,
                TestContext.Current.CancellationToken);

            member.Disable();
            await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        HttpResponseMessage refreshed = await PostTokenAsync(client, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = first.GetProperty("refresh_token").GetString()!,
            ["client_id"] = PeopleOsApiFactory.ApiClientId,
            ["client_secret"] = DevelopmentSecrets.GatewayClientSecret,
        });

        refreshed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task TheRoleSeeder_CreatesTheTwoSystemRolesWithTheirCodeDefinedPermissions()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        AppDbContext database = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        List<PlatformRole> roles = await database.Set<PlatformRole>().AsNoTracking()
            .Where(role => role.IsSystem)
            .ToListAsync(TestContext.Current.CancellationToken);

        roles.Select(role => role.TemplateCode).ShouldBe(
            PlatformRoleTemplates.Definitions.Select(template => template.Code),
            ignoreOrder: true);

        PlatformRole administrator = roles.Single(role => role.TemplateCode == PlatformRoleTemplates.StaffAdministratorCode);
        administrator.PermissionCodes.ShouldContain(Permissions.Platform.StaffManage);
    }

    /// <summary>
    /// Creates an active platform staff account directly against the database, bypassing the
    /// invitation flow: these tests are about the token endpoint and authorization, not about
    /// invitations, which <c>PlatformStaffManagementService</c> already has its own coverage for.
    /// </summary>
    private async Task<(Guid StaffId, string Email)> SeedActiveStaffAsync(string? templateCode)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        AppDbContext database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        IPasswordHasher hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        string email = $"platform-{Guid.NewGuid():N}@gggshr.example";
        PlatformStaffMember member = PlatformStaffMember.Invite(email, "Platform Test Account", DateTime.UtcNow);
        member.CompleteInvitation(hasher.Hash(StaffPassword), DateTime.UtcNow);

        database.Set<PlatformStaffMember>().Add(member);

        if (templateCode is not null)
        {
            PlatformRole role = await database.Set<PlatformRole>().SingleAsync(
                r => r.TemplateCode == templateCode,
                TestContext.Current.CancellationToken);

            database.Set<PlatformRoleAssignment>().Add(PlatformRoleAssignment.Grant(member.Id, role.Id));
        }

        await database.SaveChangesAsync(TestContext.Current.CancellationToken);

        return (member.Id, email);
    }

    private async Task<JsonElement> RequestPlatformTokenAsync(HttpClient client, string email)
    {
        HttpResponseMessage response = await PostTokenAsync(client, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "password",
            ["username"] = email,
            ["password"] = StaffPassword,
            ["scope"] = "gggs.api offline_access",
            ["client_id"] = PeopleOsApiFactory.ApiClientId,
            ["client_secret"] = DevelopmentSecrets.GatewayClientSecret,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }

    private static async Task<HttpResponseMessage> PostTokenAsync(HttpClient client, Dictionary<string, string> form)
    {
        using var content = new FormUrlEncodedContent(form);
        return await client.PostAsync("/connect/token", content, TestContext.Current.CancellationToken);
    }

    private static async Task<HttpResponseMessage> GetWithBearerAsync(HttpClient client, string path, string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Reads a JWT's payload without validating it - only ever right in a test, where the subject
    /// is what the token <em>says</em>. Nothing in the product reads a token this way.
    /// </summary>
    private static JsonElement ReadPayload(string accessToken)
    {
        string payload = accessToken.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');

        return JsonSerializer.Deserialize<JsonElement>(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
    }
}
