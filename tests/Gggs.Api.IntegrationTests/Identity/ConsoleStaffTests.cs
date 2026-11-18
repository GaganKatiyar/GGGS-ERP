using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gggs.Data;
using Gggs.Modules.Identity.Application.Authentication;
using Gggs.Modules.Identity.Application.PlatformStaff;
using Gggs.Modules.Identity.Domain.PlatformStaff;
using Gggs.Modules.Identity.Endpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Gggs.Api.IntegrationTests;

/// <summary>
/// Signing in to the platform console and managing the platform team, through the console's own
/// server. Its sign-in accepts platform staff only - never an organisation's user.
/// <para>
/// The rule refusing to leave nobody holding <c>platform.staff.manage</c> is not tested here: that
/// table has no tenant to isolate it, so "am I the only one" depends on what else ran first.
/// <c>PlatformStaffManagementServiceTests</c> (unit) pins it instead.
/// </para>
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class ConsoleStaffTests(PeopleOsApiFactory factory)
{
    private const string Password = "a platform administrator passphrase";

    private PlatformConsoleFactory Console => factory.PlatformConsole;

    [Fact]
    public async Task PlatformStaff_SignInToTheConsole_AndGetAPlatformSession()
    {
        (_, string email) = await SeedStaffAsync(PlatformRoleTemplates.StaffAdministratorCode);
        using HttpClient client = await Console.SignInAsync(email, Password);

        JsonElement session = await client.GetFromJsonAsync<JsonElement>("/bff/user", TestContext.Current.CancellationToken);

        session.GetProperty("email").GetString().ShouldBe(email);
        session.GetProperty("permissions").EnumerateArray()
            .Select(grant => grant.GetProperty("code").GetString())
            .ShouldContain("platform.staff.manage");
    }

    [Fact]
    public async Task AnOrganisationsUser_CannotSignInToTheConsole()
    {
        using HttpClient client = Console.CreateWebClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/bff/login",
            new { Email = PeopleOsApiFactory.AdminEmail, Password = PeopleOsApiFactory.AdminPassword, TenantCode = PeopleOsApiFactory.SeededTenantCode },
            TestContext.Current.CancellationToken);

        BffLoginResponse? body = await response.Content.ReadFromJsonAsync<BffLoginResponse>(TestContext.Current.CancellationToken);
        body!.Succeeded.ShouldBeFalse();
        body.ErrorCode.ShouldBe(SignInErrorCodes.InvalidCredentials);
    }

    [Fact]
    public async Task AStaffMemberWithNoRoles_CanSignInButOpensNothing()
    {
        (_, string email) = await SeedStaffAsync(templateCode: null);
        using HttpClient client = await Console.SignInAsync(email, Password);

        HttpResponseMessage response = await client.GetAsync("/api/v1/platform/staff", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AnAdministratorCannotDisableTheirOwnAccount()
    {
        (Guid staffId, string email) = await SeedStaffAsync(PlatformRoleTemplates.StaffAdministratorCode);
        using HttpClient client = await Console.SignInAsync(email, Password);

        HttpResponseMessage response = await client.PostAsync(
            $"/api/v1/platform/staff/{staffId}/disable", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("CANNOT_DISABLE_SELF");
    }

    [Fact]
    public async Task AnInvitedStaffMember_SetsAPasswordFromTheLink_AndSignsIn()
    {
        (_, string adminEmail) = await SeedStaffAsync(PlatformRoleTemplates.StaffAdministratorCode);
        using HttpClient admin = await Console.SignInAsync(adminEmail, Password);

        JsonElement roles = await admin.GetFromJsonAsync<JsonElement>("/api/v1/platform/staff/roles", TestContext.Current.CancellationToken);
        Guid support = roles.EnumerateArray()
            .Single(role => role.GetProperty("code").GetString() == PlatformRoleTemplates.SupportCode)
            .GetProperty("id").GetGuid();

        string email = $"new-staff-{Guid.NewGuid():N}@gggshr.example";
        HttpResponseMessage invited = await admin.PostAsJsonAsync(
            "/api/v1/platform/staff/invite",
            new { Email = email, DisplayName = "New Staff", RoleIds = new[] { support } },
            TestContext.Current.CancellationToken);
        invited.StatusCode.ShouldBe(HttpStatusCode.Created);
        JsonElement invitation = await invited.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        using HttpClient anonymous = Console.CreateWebClient();
        HttpResponseMessage completed = await anonymous.PostAsJsonAsync(
            "/api/v1/platform/staff/complete-invitation",
            new
            {
                PlatformStaffId = invitation.GetProperty("platformStaffId").GetGuid(),
                Token = invitation.GetProperty("invitationToken").GetString(),
                NewPassword = "correct horse battery zebra 2026",
            },
            TestContext.Current.CancellationToken);
        completed.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using HttpClient newcomer = await Console.SignInAsync(email, "correct horse battery zebra 2026");
        (await newcomer.GetAsync("/api/v1/platform/organisations", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // The invitation is in the activity log.
        await using AppDbContext database = factory.CreateContext();
        (await database.Set<Gggs.Modules.Platform.Domain.Console.PlatformAuditEntry>()
            .AnyAsync(entry => entry.Action == "staff.invited" && entry.Summary.Contains(email), TestContext.Current.CancellationToken))
            .ShouldBeTrue();
    }

    [Fact]
    public async Task Setup_IsClosedOnceAnyStaffExist_AndRefusesAWrongKey()
    {
        await SeedStaffAsync(PlatformRoleTemplates.SupportCode);
        using HttpClient client = Console.CreateWebClient();

        JsonElement state = await client.GetFromJsonAsync<JsonElement>("/api/v1/platform/setup", TestContext.Current.CancellationToken);
        state.GetProperty("open").GetBoolean().ShouldBeFalse();

        HttpResponseMessage wrongKey = await client.PostAsJsonAsync(
            "/api/v1/platform/setup",
            new { SetupKey = "not the key", DisplayName = "Intruder", Email = "intruder@example.test", Password = "an intruder passphrase" },
            TestContext.Current.CancellationToken);
        wrongKey.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        HttpResponseMessage closed = await client.PostAsJsonAsync(
            "/api/v1/platform/setup",
            new { SetupKey = PlatformConsoleFactory.SetupKey, DisplayName = "Late Admin", Email = "late@example.test", Password = "a late administrator passphrase" },
            TestContext.Current.CancellationToken);
        closed.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await closed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("SETUP_CLOSED");
    }

    private async Task<(Guid StaffId, string Email)> SeedStaffAsync(string? templateCode)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        AppDbContext database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        IPasswordHasher hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        string email = $"platform-console-{Guid.NewGuid():N}@gggshr.example";
        PlatformStaffMember member = PlatformStaffMember.Invite(email, "Console Test Account", DateTime.UtcNow);
        member.CompleteInvitation(hasher.Hash(Password), DateTime.UtcNow);
        database.Set<PlatformStaffMember>().Add(member);

        if (templateCode is not null)
        {
            PlatformRole role = await database.Set<PlatformRole>().SingleAsync(r => r.TemplateCode == templateCode, TestContext.Current.CancellationToken);
            database.Set<PlatformRoleAssignment>().Add(PlatformRoleAssignment.Grant(member.Id, role.Id));
        }

        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (member.Id, email);
    }
}
