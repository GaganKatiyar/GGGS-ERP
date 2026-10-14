using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Gggs.BuildingBlocks.Application;
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
/// The self-disable safety rule in <see cref="PlatformStaffManagementService"/>, exercised through
/// the real endpoint, the real token and the real database.
/// <para>
/// The other safety rule - refusing to leave nobody holding <c>platform.staff.manage</c> - is not
/// tested here. That table has no tenant to isolate it, so every test in this shared fixture can
/// see every other test's seeded administrators; "am I the only one" is true or false depending on
/// what else happened to run first. <c>PlatformStaffManagementServiceTests</c> (unit) pins that rule
/// instead, against repositories this test controls completely.
/// </para>
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class PlatformStaffManagementTests(PeopleOsApiFactory factory)
{
    private const string Password = "a platform administrator passphrase";

    [Fact]
    public async Task AnAdministratorCannotDisableTheirOwnAccount()
    {
        (Guid staffId, HttpClient client) = await SeedSignedInAdministratorAsync();
        using HttpClient _ = client;

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/platform-staff/{staffId}/disable");
        HttpResponseMessage response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldContain("CANNOT_DISABLE_SELF");
    }

    private async Task<Guid> SeedStaffAsync(string? templateCode)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        AppDbContext database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        IPasswordHasher hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        string email = $"platform-mgmt-{Guid.NewGuid():N}@gggshr.example";
        PlatformStaffMember member = PlatformStaffMember.Invite(email, "Platform Management Test", DateTime.UtcNow);
        member.CompleteInvitation(hasher.Hash(Password), DateTime.UtcNow);

        database.Set<PlatformStaffMember>().Add(member);

        if (templateCode is not null)
        {
            PlatformRole role = await database.Set<PlatformRole>().SingleAsync(
                r => r.TemplateCode == templateCode,
                TestContext.Current.CancellationToken);

            database.Set<PlatformRoleAssignment>().Add(PlatformRoleAssignment.Grant(member.Id, role.Id));
        }

        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        return member.Id;
    }

    private async Task<string> EmailForAsync(Guid staffId)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        AppDbContext database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        PlatformStaffMember member = await database.Set<PlatformStaffMember>().AsNoTracking().SingleAsync(
            m => m.Id == staffId,
            TestContext.Current.CancellationToken);

        return member.Email;
    }

    private async Task<(Guid StaffId, HttpClient Client)> SeedSignedInAdministratorAsync()
    {
        Guid staffId = await SeedStaffAsync(PlatformRoleTemplates.StaffAdministratorCode);
        string email = await EmailForAsync(staffId);
        return (staffId, await SignInAsync(email));
    }

    private async Task<HttpClient> SignInAsync(string email)
    {
        HttpClient client = factory.CreateWebClient();

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "password",
            ["username"] = email,
            ["password"] = Password,
            ["scope"] = "gggs.api offline_access",
            ["client_id"] = PeopleOsApiFactory.ApiClientId,
            ["client_secret"] = DevelopmentSecrets.GatewayClientSecret,
        });

        HttpResponseMessage response = await client.PostAsync("/connect/token", content, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            body.GetProperty("access_token").GetString());

        return client;
    }
}
