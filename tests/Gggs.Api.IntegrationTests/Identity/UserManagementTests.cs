using System.Net;
using System.Net.Http.Json;
using Gggs.BuildingBlocks.Security;
using Shouldly;

namespace Gggs.Api.IntegrationTests;

/// <summary>
/// Inviting people, changing passwords and moving roles around.
/// <para>
/// The rules that matter most here stop an organization locking itself out. A tenant with no
/// remaining administrator cannot be repaired by its own administrators, by definition - the only
/// remedy is us reaching into their database, which is exactly the access this platform is built
/// to avoid needing.
/// </para>
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class UserManagementTests(PeopleOsApiFactory factory)
{
    [Fact]
    public async Task InvitingSomeone_ReturnsATokenAndLeavesThemUnableToSignInYet()
    {
        using HttpClient client = await factory.CreateAdminClientAsync();

        InviteResponse invite = await InviteAsync(client, "newjoiner@testco.example", "New Joiner");

        invite.InvitationToken.ShouldNotBeNullOrWhiteSpace();
        invite.ExpiresDate.ShouldBeGreaterThan(DateTime.UtcNow);

        // No password yet, so the account exists but cannot be used.
        using HttpClient anonymous = factory.CreateWebClient();
        HttpResponseMessage signIn = await anonymous.PostAsJsonAsync(
            "/bff/login",
            new { Email = "newjoiner@testco.example", Password = "anything", TenantCode = PeopleOsApiFactory.SeededTenantCode },
            TestContext.Current.CancellationToken);

        (await signIn.Content.ReadFromJsonAsync<SignInBody>(TestContext.Current.CancellationToken))!
            .Succeeded.ShouldBeFalse();
    }

    [Fact]
    public async Task CompletingAnInvitation_LetsThePersonSignIn()
    {
        using HttpClient client = await factory.CreateAdminClientAsync();
        InviteResponse invite = await InviteAsync(client, "completes@testco.example", "Completes Invite");

        using HttpClient anonymous = factory.CreateWebClient();

        HttpResponseMessage completion = await anonymous.PostAsJsonAsync(
            "/api/v1/auth/complete-invitation",
            new { TenantCode = PeopleOsApiFactory.SeededTenantCode, invite.UserId, Token = invite.InvitationToken, NewPassword = "a good long passphrase" },
            TestContext.Current.CancellationToken);

        completion.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        HttpResponseMessage signIn = await anonymous.PostAsJsonAsync(
            "/bff/login",
            new { Email = "completes@testco.example", Password = "a good long passphrase", TenantCode = PeopleOsApiFactory.SeededTenantCode },
            TestContext.Current.CancellationToken);

        (await signIn.Content.ReadFromJsonAsync<SignInBody>(TestContext.Current.CancellationToken))!
            .Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task AnInvitationTokenCannotBeUsedTwice()
    {
        // A link that still works after the account is live is a standing way in for anyone who
        // saw the email - including whoever the invitation was forwarded to.
        using HttpClient client = await factory.CreateAdminClientAsync();
        InviteResponse invite = await InviteAsync(client, "reuse@testco.example", "Reuse Probe");

        using HttpClient anonymous = factory.CreateWebClient();
        object body = new { TenantCode = PeopleOsApiFactory.SeededTenantCode, invite.UserId, Token = invite.InvitationToken, NewPassword = "a good long passphrase" };

        await anonymous.PostAsJsonAsync("/api/v1/auth/complete-invitation", body, TestContext.Current.CancellationToken);
        HttpResponseMessage second = await anonymous.PostAsJsonAsync(
            "/api/v1/auth/complete-invitation", body, TestContext.Current.CancellationToken);

        second.StatusCode.ShouldBe(HttpStatusCode.UnprocessableContent);
    }

    [Fact]
    public async Task AWrongInvitationTokenIsRefused()
    {
        using HttpClient client = await factory.CreateAdminClientAsync();
        InviteResponse invite = await InviteAsync(client, "wrongtoken@testco.example", "Wrong Token");

        using HttpClient anonymous = factory.CreateWebClient();

        HttpResponseMessage response = await anonymous.PostAsJsonAsync(
            "/api/v1/auth/complete-invitation",
            new { TenantCode = PeopleOsApiFactory.SeededTenantCode, invite.UserId, Token = "not-the-token", NewPassword = "a good long passphrase" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableContent);
    }

    [Fact]
    public async Task APasswordThatBreachesThePolicyIsRefused()
    {
        using HttpClient client = await factory.CreateAdminClientAsync();
        InviteResponse invite = await InviteAsync(client, "shortpw@testco.example", "Short Password");

        using HttpClient anonymous = factory.CreateWebClient();

        HttpResponseMessage response = await anonymous.PostAsJsonAsync(
            "/api/v1/auth/complete-invitation",
            new { TenantCode = PeopleOsApiFactory.SeededTenantCode, invite.UserId, Token = invite.InvitationToken, NewPassword = "short" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableContent);

        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldContain("PASSWORD_REJECTED");
    }

    [Fact]
    public async Task InvitingTheSameAddressTwiceIsRefused()
    {
        using HttpClient client = await factory.CreateAdminClientAsync();
        await InviteAsync(client, "duplicate@testco.example", "First");

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/users/invite",
            new { Email = "duplicate@testco.example", DisplayName = "Second", RoleIds = Array.Empty<Guid>() },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableContent);
    }

    [Fact]
    public async Task AnAdministratorCannotDisableTheirOwnAccount()
    {
        // Doing so would leave them unable to undo it, and is almost always a misclick.
        using HttpClient client = await factory.CreateAdminClientAsync();

        HttpResponseMessage response = await client.PostAsync(
            $"/api/v1/users/{PeopleOsApiFactory.AdminUserId}/disable",
            content: null,
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableContent);

        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldContain("CANNOT_DISABLE_SELF");
    }

    [Fact]
    public async Task ChangingYourOwnPassword_RequiresTheCurrentOne()
    {
        // Otherwise anyone at an unlocked laptop can take the account over and lock the owner out.
        using HttpClient client = await factory.CreateAdminClientAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/users/me/password",
            new { CurrentPassword = "not-the-current-one", NewPassword = "another good passphrase" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ListingUsersNeverExposesCredentialMaterial()
    {
        // A list endpoint is exactly where a hash or a token would end up in a log or a cache.
        using HttpClient client = await factory.CreateAdminClientAsync();

        string body = await client.GetStringAsync("/api/v1/users", TestContext.Current.CancellationToken);

        body.ShouldNotContain("passwordHash", Case.Insensitive);
        body.ShouldNotContain("securityStamp", Case.Insensitive);
        body.ShouldNotContain("invitationToken", Case.Insensitive);
    }

    [Fact]
    public async Task TheRoleEditorNeverOffersPlatformPermissions()
    {
        // A tenant administrator must not even be shown the permission that lists our customers.
        using HttpClient client = await factory.CreateAdminClientAsync();

        string body = await client.GetStringAsync("/api/v1/permissions", TestContext.Current.CancellationToken);

        body.ShouldContain(Permissions.Employee.View);
        body.ShouldNotContain(Permissions.Platform.TenantManage);
    }

    [Fact]
    public async Task ARoleCannotBeGrantedAPermissionThePlatformDoesNotDefine()
    {
        using HttpClient client = await factory.CreateAdminClientAsync();

        HttpResponseMessage created = await client.PostAsJsonAsync(
            "/api/v1/roles",
            new { Name = $"Typo Probe {Guid.CreateVersion7():N}", Description = "probe" },
            TestContext.Current.CancellationToken);

        RoleBody role = (await created.Content.ReadFromJsonAsync<RoleBody>(TestContext.Current.CancellationToken))!;

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/roles/{role.Id}/permissions",
            new { PermissionCode = "leave.request.approv", Scope = "Tenant" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableContent);

        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldContain("PERMISSION_UNKNOWN");
    }

    private async Task<InviteResponse> InviteAsync(HttpClient client, string email, string displayName)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/users/invite",
            new { Email = email, DisplayName = displayName, RoleIds = Array.Empty<Guid>() },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<InviteResponse>(TestContext.Current.CancellationToken))!;
    }

    private sealed record InviteResponse(Guid UserId, string InvitationToken, DateTime ExpiresDate);

    private sealed record SignInBody(bool Succeeded, string? ErrorCode);

    private sealed record RoleBody(Guid Id, string Name);
}
