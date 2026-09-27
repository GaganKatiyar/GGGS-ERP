using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Gggs.BuildingBlocks.Application;
using Gggs.Identity.Application.Authentication;
using Gggs.Identity.Application.PlatformStaff;
using Gggs.Identity.Domain.PlatformStaff;
using Gggs.Identity.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Gggs.Identity.IntegrationTests;

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
[Collection(nameof(IdentityApiCollection))]
public sealed class PlatformStaffManagementTests(IdentityApiFactory factory)
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
        IdentityDbContext database = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        IPasswordHasher hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        string email = $"platform-mgmt-{Guid.NewGuid():N}@gggshr.example";
        PlatformStaffMember member = PlatformStaffMember.Invite(email, "Platform Management Test", DateTime.UtcNow);
        member.CompleteInvitation(hasher.Hash(Password), DateTime.UtcNow);

        database.PlatformStaff.Add(member);

        if (templateCode is not null)
        {
            PlatformRole role = await database.PlatformRoles.SingleAsync(
                r => r.TemplateCode == templateCode,
                TestContext.Current.CancellationToken);

            database.PlatformRoleAssignments.Add(PlatformRoleAssignment.Grant(member.Id, role.Id));
        }

        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        return member.Id;
    }

    private async Task<string> EmailForAsync(Guid staffId)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        IdentityDbContext database = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        PlatformStaffMember member = await database.PlatformStaff.AsNoTracking().SingleAsync(
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
            ["client_id"] = "gggs-bff",
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
