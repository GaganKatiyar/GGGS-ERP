using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Notification.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Gggs.Api.IntegrationTests;

/// <summary>Forgotten passwords: a link that works once, for an hour, and tells nobody who has an account.</summary>
[Collection(nameof(ApiCollection))]
public sealed partial class PasswordResetTests(PeopleOsApiFactory factory)
{
    private const string OldPassword = "a scoped test passphrase";
    private const string NewPassword = "a brand new passphrase seven";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ALink_SetsANewPassword_Once_AndSignsTheOldSessionOut()
    {
        await WithRecordedEmailAsync(async () =>
        {
            string email = Address();
            using HttpClient session = await factory.CreateScopedUserClientAsync(email, null, (Permissions.Notification.InboxView, DataScope.Own));
            (await session.GetAsync("/api/v1/me/notification-preferences", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

            (string userId, string token) = await RequestLinkAsync(email);

            // Too weak: refused, and the link still works.
            using HttpClient anonymous = factory.CreateRawClient();
            HttpResponseMessage weak = await anonymous.PostAsJsonAsync("/api/v1/auth/reset-password", new { TenantCode = PeopleOsApiFactory.SeededTenantCode, UserId = userId, Token = token, NewPassword = "short" }, Ct);
            (await CoreHrApi.ErrorCodeAsync(weak)).ShouldBe("PASSWORD_REJECTED");

            (await Reset(anonymous, userId, token)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

            (await SignInAsync(email, OldPassword)).ShouldBeFalse("the old password is gone");
            (await SignInAsync(email, NewPassword)).ShouldBeTrue();
            (await session.GetAsync("/api/v1/me/notification-preferences", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized, "signed out everywhere");

            // Only what came before the reset: signing in again works, and stays working.
            using HttpClient fresh = await factory.CreateSignedInClientAsync(email, NewPassword, PeopleOsApiFactory.SeededTenantCode);
            (await fresh.GetAsync("/api/v1/me/notification-preferences", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

            // It works once.
            (await CoreHrApi.ErrorCodeAsync(await Reset(anonymous, userId, token))).ShouldBe("PASSWORD_RESET_INVALID");

            // The owner is told, in case it was not them.
            await factory.DeliverEventsAsync();
            IReadOnlyDictionary<string, string> notice = factory.Recorder.Sent.Single(message => message.GetValueOrDefault("to") == email && message.GetValueOrDefault("subject") == "Your password was changed");
            notice["body"].ShouldContain("signed out everywhere");
        });
    }

    [Fact]
    public async Task ARefreshTokenFromBeforeTheReset_CannotBeRenewed_AndOneFromAfterCan()
    {
        await WithRecordedEmailAsync(async () =>
        {
            string email = Address();
            using HttpClient session = await factory.CreateScopedUserClientAsync(email, null, (Permissions.Notification.InboxView, DataScope.Own));
            using HttpClient client = factory.CreateWebClient();
            string before = await RefreshTokenAsync(client, email, OldPassword);

            (string userId, string token) = await RequestLinkAsync(email);
            using HttpClient anonymous = factory.CreateRawClient();
            (await Reset(anonymous, userId, token)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

            (await RenewAsync(client, before)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

            // The stored time is to the second, so a sign-in needs a clear second to count as after the reset.
            await Task.Delay(TimeSpan.FromSeconds(1.1), Ct);
            string after = await RefreshTokenAsync(client, email, NewPassword);
            (await RenewAsync(client, after)).StatusCode.ShouldBe(HttpStatusCode.OK);
        });
    }

    [Fact]
    public async Task TheStoredEmail_NeverHoldsTheLink()
    {
        await WithRecordedEmailAsync(async () =>
        {
            string email = Address();
            using HttpClient session = await factory.CreateScopedUserClientAsync(email, null, (Permissions.Notification.InboxView, DataScope.Own));
            (_, string token) = await RequestLinkAsync(email);

            await factory.RunAsWorkerAsync(PeopleOsApiFactory.SeededTenantId, async provider =>
            {
                EmailMessage record = await provider.GetRequiredService<AppDbContext>().Set<EmailMessage>().AsNoTracking().SingleAsync(row => row.ToAddress == email, Ct);
                record.Status.ShouldBe(EmailStatus.Sent);
                record.Body.ShouldNotContain(token);
                record.Body.ShouldNotContain("/reset-password");
                record.Body.ShouldContain("••••");
            });
        });
    }

    [Fact]
    public async Task TheAnswer_IsTheSame_WhoeverAsks()
    {
        await WithRecordedEmailAsync(async () =>
        {
            string real = Address();
            using HttpClient session = await factory.CreateScopedUserClientAsync(real, null, (Permissions.Notification.InboxView, DataScope.Own));
            using HttpClient anonymous = factory.CreateRawClient();

            foreach (object body in new object[]
            {
                new { TenantCode = PeopleOsApiFactory.SeededTenantCode, Email = real },
                new { TenantCode = PeopleOsApiFactory.SeededTenantCode, Email = Address() },
                new { TenantCode = "no-such-organisation", Email = real },
                new { TenantCode = PeopleOsApiFactory.OtherTenantCode, Email = real },
                new { TenantCode = PeopleOsApiFactory.SeededTenantCode, Email = string.Empty },
            })
            {
                HttpResponseMessage response = await anonymous.PostAsJsonAsync("/api/v1/auth/forgot-password", body, Ct);
                response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
                (await response.Content.ReadAsStringAsync(Ct)).ShouldBeEmpty();
            }

            await factory.DeliverEventsAsync();
            factory.Recorder.Sent.Count(message => message.GetValueOrDefault("to") == real).ShouldBe(1, "only the right organisation sends anything");
        });
    }

    [Fact]
    public async Task AskingAgainStraightAway_SendsNothingMore()
    {
        await WithRecordedEmailAsync(async () =>
        {
            string email = Address();
            using HttpClient session = await factory.CreateScopedUserClientAsync(email, null, (Permissions.Notification.InboxView, DataScope.Own));
            await RequestLinkAsync(email);

            using HttpClient anonymous = factory.CreateRawClient();
            (await anonymous.PostAsJsonAsync("/api/v1/auth/forgot-password", new { TenantCode = PeopleOsApiFactory.SeededTenantCode, Email = email }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            await factory.DeliverEventsAsync();

            factory.Recorder.Sent.Count(message => message.GetValueOrDefault("to") == email).ShouldBe(1);
        });
    }

    [Fact]
    public async Task ADisabledAccount_GetsNoLink_AndALinkDiesWithTheAccount()
    {
        await WithRecordedEmailAsync(async () =>
        {
            using HttpClient admin = await factory.CreateAdminClientAsync();
            string email = Address();
            using HttpClient session = await factory.CreateScopedUserClientAsync(email, null, (Permissions.Notification.InboxView, DataScope.Own));
            (string userId, string token) = await RequestLinkAsync(email);

            (await admin.PostAsync($"/api/v1/users/{userId}/disable", null, Ct)).EnsureSuccessStatusCode();

            using HttpClient anonymous = factory.CreateRawClient();
            (await CoreHrApi.ErrorCodeAsync(await Reset(anonymous, userId, token))).ShouldBe("PASSWORD_RESET_INVALID");
            (await anonymous.PostAsJsonAsync("/api/v1/auth/forgot-password", new { TenantCode = PeopleOsApiFactory.SeededTenantCode, Email = email }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            await factory.DeliverEventsAsync();
            factory.Recorder.Sent.Count(message => message.GetValueOrDefault("to") == email).ShouldBe(1, "only the link from before it was disabled");
        });
    }

    [Fact]
    public async Task AnInvitationLink_CannotResetAPassword_NorAResetLinkCompleteAnInvitation()
    {
        await WithRecordedEmailAsync(async () =>
        {
            using HttpClient admin = await factory.CreateAdminClientAsync();
            using HttpClient anonymous = factory.CreateRawClient();

            // Someone invited but not yet set up gets no reset link, and their invitation token is no use here.
            string invited = Address();
            JsonElement invitation = await (await admin.PostAsJsonAsync("/api/v1/users/invite", new { Email = invited, DisplayName = "Invited Person", UserType = "TenantUser", RoleIds = Array.Empty<Guid>() }, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
            if (invitation.TryGetProperty("userId", out JsonElement invitedId) && invitation.TryGetProperty("invitationToken", out JsonElement invitationToken))
            {
                (await CoreHrApi.ErrorCodeAsync(await Reset(anonymous, invitedId.GetGuid().ToString(), invitationToken.GetString()!))).ShouldBe("PASSWORD_RESET_INVALID");
            }

            int before = factory.Recorder.Sent.Count(message => message.GetValueOrDefault("to") == invited && message.GetValueOrDefault("subject") == "Choose a new password");
            await anonymous.PostAsJsonAsync("/api/v1/auth/forgot-password", new { TenantCode = PeopleOsApiFactory.SeededTenantCode, Email = invited }, Ct);
            await factory.DeliverEventsAsync();
            factory.Recorder.Sent.Count(message => message.GetValueOrDefault("to") == invited && message.GetValueOrDefault("subject") == "Choose a new password").ShouldBe(before);

            // A reset token cannot pass as an invitation.
            string email = Address();
            using HttpClient session = await factory.CreateScopedUserClientAsync(email, null, (Permissions.Notification.InboxView, DataScope.Own));
            (string userId, string token) = await RequestLinkAsync(email);
            HttpResponseMessage asInvitation = await anonymous.PostAsJsonAsync("/api/v1/auth/complete-invitation", new { TenantCode = PeopleOsApiFactory.SeededTenantCode, UserId = userId, Token = token, NewPassword }, Ct);
            asInvitation.IsSuccessStatusCode.ShouldBeFalse();
            (await SignInAsync(email, OldPassword)).ShouldBeTrue("the password did not change");
        });
    }

    private static string Address() => $"{Guid.NewGuid():N}@reset.example";

    private async Task<(string UserId, string Token)> RequestLinkAsync(string email)
    {
        using HttpClient anonymous = factory.CreateRawClient();
        (await anonymous.PostAsJsonAsync("/api/v1/auth/forgot-password", new { TenantCode = PeopleOsApiFactory.SeededTenantCode, Email = email }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await factory.DeliverEventsAsync();

        IReadOnlyDictionary<string, string> sent = factory.Recorder.Sent.Single(message => message.GetValueOrDefault("to") == email && message.GetValueOrDefault("subject") == "Choose a new password");
        Match link = ResetLink().Match(sent["body"]);
        link.Success.ShouldBeTrue(sent["body"]);
        return (link.Groups["user"].Value, Uri.UnescapeDataString(link.Groups["token"].Value));
    }

    private static Task<HttpResponseMessage> Reset(HttpClient client, string userId, string token)
        => client.PostAsJsonAsync("/api/v1/auth/reset-password", new { TenantCode = PeopleOsApiFactory.SeededTenantCode, UserId = userId, Token = token, NewPassword }, Ct);

    private static async Task<string> RefreshTokenAsync(HttpClient client, string email, string password)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "password",
            ["username"] = email,
            ["password"] = password,
            ["tenant_code"] = PeopleOsApiFactory.SeededTenantCode,
            ["scope"] = "gggs.api offline_access",
            ["client_id"] = PeopleOsApiFactory.ApiClientId,
            ["client_secret"] = DevelopmentSecrets.GatewayClientSecret,
        });
        HttpResponseMessage response = await client.PostAsync("/connect/token", form, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("refresh_token").GetString()!;
    }

    private static async Task<HttpResponseMessage> RenewAsync(HttpClient client, string refreshToken)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = PeopleOsApiFactory.ApiClientId,
            ["client_secret"] = DevelopmentSecrets.GatewayClientSecret,
        });
        return await client.PostAsync("/connect/token", form, Ct);
    }

    private async Task<bool> SignInAsync(string email, string password)
    {
        using HttpClient browser = factory.CreateWebClient();
        HttpResponseMessage response = await browser.PostAsJsonAsync("/bff/login", new { TenantCode = PeopleOsApiFactory.SeededTenantCode, Email = email, Password = password }, Ct);
        return response.IsSuccessStatusCode && (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("succeeded").GetBoolean();
    }

    /// <summary>Emails go through a recording connection, the only way a test can read the link.</summary>
    private async Task WithRecordedEmailAsync(Func<Task> test)
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        JsonElement connection = await (await admin.PostAsJsonAsync("/api/v1/integrations/connections", new
        {
            AdapterKey = RecordingEmailAdapter.AdapterKey,
            Name = $"Reset mail {Guid.NewGuid():N}"[..20],
            Settings = new Dictionary<string, string> { ["fail"] = "no" },
        }, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        (await admin.PutAsJsonAsync("/api/v1/studio/channels/email", new { ConnectionId = connection.GetProperty("id").GetGuid() }, Ct)).EnsureSuccessStatusCode();
        try
        {
            await test();
        }
        finally
        {
            (await admin.PutAsJsonAsync("/api/v1/studio/channels/email", new { ConnectionId = (Guid?)null }, Ct)).EnsureSuccessStatusCode();
        }
    }

    [GeneratedRegex(@"/reset-password\?tenantCode=[^&]+&userId=(?<user>[0-9a-fA-F-]+)&token=(?<token>[^\s]+)")]
    private static partial Regex ResetLink();
}
