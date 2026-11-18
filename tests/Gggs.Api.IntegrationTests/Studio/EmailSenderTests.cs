using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gggs.Data;
using Gggs.Modules.Notification.Contracts;
using Gggs.Modules.Notification.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Gggs.Api.IntegrationTests;

/// <summary>
/// Studio > Email senders and Email log: each module's emails come from its own sender, filled in from the
/// default sender; a paused module sends none; an email nothing could carry is "logged", not "sent", and can
/// be sent again once a connection is set.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class EmailSenderTests(PeopleOsApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_module_sends_as_its_own_sender_and_the_rest_as_the_default()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        (Guid company, string companyName) = await ConnectionAsync(admin);
        (Guid rewards, string rewardsName) = await ConnectionAsync(admin);
        try
        {
            await SaveAsync(admin, "default", new { ConnectionId = company, FromName = "GGGS People Team", ReplyTo = "hr@acme.example", Signature = "GGGS People Team" });
            await SaveAsync(admin, "reward", new
            {
                ConnectionId = rewards,
                FromName = "GGGS Star Rewards",
                FromAddress = "rewards@acme.example",
                ReplyTo = "culture@acme.example",
                Bcc = "archive@acme.example",
                Signature = "Keep shining!",
                BrandColor = "#b45309",
            });

            string starred = Address();
            await NotifyAsync(NotificationTemplates.Recognized, starred, new Dictionary<string, string> { ["giverName"] = "Priya", ["typeName"] = "Customer first", ["points"] = "50", ["message"] = "Thank you" });
            IReadOnlyDictionary<string, string> reward = factory.Recorder.Sent.Single(message => message.GetValueOrDefault("to") == starred);
            reward["fromName"].ShouldBe("GGGS Star Rewards");
            reward["fromAddress"].ShouldBe("rewards@acme.example");
            reward["replyTo"].ShouldBe("culture@acme.example");
            reward["bcc"].ShouldBe("archive@acme.example");
            reward["body"].ShouldEndWith("Keep shining!");
            reward["html"].ShouldContain("#b45309");
            reward["html"].ShouldContain("GGGS Star Rewards");
            EmailMessage rewardRecord = await EmailAsync(starred);
            rewardRecord.Status.ShouldBe(EmailStatus.Sent);
            rewardRecord.ModuleKey.ShouldBe("reward");
            rewardRecord.SentAs!.ShouldContain("rewards@acme.example");
            rewardRecord.SentAs.ShouldContain(rewardsName);

            // A module without its own sender goes as the default one, through the default connection.
            string invited = Address();
            await NotifyAsync(NotificationTemplates.UserInvited, invited, new Dictionary<string, string>());
            IReadOnlyDictionary<string, string> invitation = factory.Recorder.Sent.Single(message => message.GetValueOrDefault("to") == invited);
            invitation["fromName"].ShouldBe("GGGS People Team");
            invitation["replyTo"].ShouldBe("hr@acme.example");
            invitation.ContainsKey("fromAddress").ShouldBeFalse("the default sender leaves the connection's own address");
            (await EmailAsync(invited)).SentAs!.ShouldContain(companyName);

            JsonElement view = await admin.GetFromJsonAsync<JsonElement>("/api/v1/notifications/senders", Ct);
            JsonElement rewardRow = view.GetProperty("senders").EnumerateArray().Single(row => row.GetProperty("key").GetString() == "reward");
            rewardRow.GetProperty("hasOwn").GetBoolean().ShouldBeTrue();
            rewardRow.GetProperty("templates").GetInt32().ShouldBeGreaterThan(0);
            view.GetProperty("senders").EnumerateArray().Single(row => row.GetProperty("key").GetString() == "appraisal").GetProperty("hasOwn").GetBoolean().ShouldBeFalse();
        }
        finally
        {
            await RemoveAsync(admin, "reward");
            await RemoveAsync(admin, "default");
        }
    }

    [Fact]
    public async Task A_template_can_borrow_another_modules_sender()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        (Guid connection, _) = await ConnectionAsync(admin);
        JsonElement template = await TemplateAsync(admin, NotificationTemplates.Recognized);
        try
        {
            await SaveAsync(admin, "default", new { ConnectionId = connection });
            await SaveAsync(admin, "appraisal", new { FromName = "GGGS Appraisal Desk" });
            await UpdateTemplateAsync(admin, template, senderKey: "appraisal");
            (await TemplateAsync(admin, NotificationTemplates.Recognized)).GetProperty("senderKey").GetString().ShouldBe("appraisal");

            string address = Address();
            await NotifyAsync(NotificationTemplates.Recognized, address, new Dictionary<string, string>());
            factory.Recorder.Sent.Single(message => message.GetValueOrDefault("to") == address)["fromName"].ShouldBe("GGGS Appraisal Desk");
            (await EmailAsync(address)).ModuleKey.ShouldBe("reward", "it still belongs to Star Reward in the log");

            (await CoreHrApi.ErrorCodeAsync(await PutTemplateAsync(admin, template, senderKey: "carrier-pigeon"))).ShouldBe("TEMPLATE_SENDER");
        }
        finally
        {
            await UpdateTemplateAsync(admin, template, senderKey: null);
            await RemoveAsync(admin, "appraisal");
            await RemoveAsync(admin, "default");
        }
    }

    [Fact]
    public async Task A_paused_module_sends_no_email_but_a_notice_that_must_arrive_still_goes()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        (Guid connection, _) = await ConnectionAsync(admin);
        try
        {
            await SaveAsync(admin, "default", new { ConnectionId = connection });
            await SaveAsync(admin, "reward", new { IsPaused = true });
            await SaveAsync(admin, "identity", new { IsPaused = true });

            string starred = Address();
            await NotifyAsync(NotificationTemplates.Recognized, starred, new Dictionary<string, string>());
            factory.Recorder.Sent.ShouldNotContain(message => message.GetValueOrDefault("to") == starred);
            EmailMessage paused = await EmailAsync(starred);
            paused.Status.ShouldBe(EmailStatus.Paused);
            paused.LastError!.ShouldContain("Star Reward");

            // An invitation is mandatory: pausing its module does not hold it back.
            string invited = Address();
            await NotifyAsync(NotificationTemplates.UserInvited, invited, new Dictionary<string, string>());
            (await EmailAsync(invited)).Status.ShouldBe(EmailStatus.Sent);

            (await CoreHrApi.ErrorCodeAsync(await admin.PutAsJsonAsync("/api/v1/notifications/senders/default", new { ConnectionId = connection, IsPaused = true }, Ct))).ShouldBe("EMAIL_SENDER_PAUSE");
        }
        finally
        {
            await RemoveAsync(admin, "identity");
            await RemoveAsync(admin, "reward");
            await RemoveAsync(admin, "default");
        }
    }

    [Fact]
    public async Task An_email_nothing_could_carry_is_logged_and_can_be_resent_once_a_connection_is_set()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        (Guid connection, _) = await ConnectionAsync(admin);
        string address = Address();
        try
        {
            await NotifyAsync(NotificationTemplates.Recognized, address, new Dictionary<string, string>());
            EmailMessage logged = await EmailAsync(address);
            logged.Status.ShouldBe(EmailStatus.Logged);
            logged.LastError!.ShouldContain("server log");

            JsonElement page = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/notifications/emails?module=reward&status=Logged&search={address}", Ct);
            JsonElement row = page.GetProperty("page").GetProperty("items").EnumerateArray().ShouldHaveSingleItem();
            row.GetProperty("status").GetString().ShouldBe("Logged");
            row.GetProperty("moduleName").GetString().ShouldBe("Star Reward");
            row.GetProperty("canResend").GetBoolean().ShouldBeTrue();
            row.TryGetProperty("body", out _).ShouldBeFalse("the log never shows what an email said");
            page.GetProperty("counts").GetProperty("Logged").GetInt32().ShouldBe(1);

            await SaveAsync(admin, "default", new { ConnectionId = connection });
            HttpResponseMessage resent = await admin.PostAsync($"/api/v1/notifications/emails/{logged.Id}/resend", null, Ct);
            resent.StatusCode.ShouldBe(HttpStatusCode.OK, await resent.Content.ReadAsStringAsync(Ct));
            await factory.DeliverEventsAsync();

            (await EmailAsync(address)).Status.ShouldBe(EmailStatus.Sent);
            factory.Recorder.Sent.ShouldContain(message => message.GetValueOrDefault("to") == address);
            (await CoreHrApi.ErrorCodeAsync(await admin.PostAsync($"/api/v1/notifications/emails/{logged.Id}/resend", null, Ct))).ShouldBe("EMAIL_RESEND");
        }
        finally
        {
            await RemoveAsync(admin, "default");
        }
    }

    [Fact]
    public async Task A_sender_takes_only_real_addresses_colours_and_email_connections()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        (Guid sms, _) = await ConnectionAsync(admin, RecordingSmsAdapter.AdapterKey);

        (await CoreHrApi.ErrorCodeAsync(await admin.PutAsJsonAsync("/api/v1/notifications/senders/onboarding", new { FromAddress = "not an address" }, Ct))).ShouldBe("EMAIL_SENDER_ADDRESS");
        (await CoreHrApi.ErrorCodeAsync(await admin.PutAsJsonAsync("/api/v1/notifications/senders/onboarding", new { BrandColor = "red" }, Ct))).ShouldBe("EMAIL_SENDER_COLOR");
        (await CoreHrApi.ErrorCodeAsync(await admin.PutAsJsonAsync("/api/v1/notifications/senders/onboarding", new { ConnectionId = sms }, Ct))).ShouldBe("EMAIL_SENDER_CONNECTION");
        (await admin.PutAsJsonAsync("/api/v1/notifications/senders/carrier-pigeon", new { FromName = "Coo" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CoreHrApi.ErrorCodeAsync(await admin.PostAsJsonAsync("/api/v1/notifications/senders/onboarding/test", new { To = "someone@acme.example" }, Ct))).ShouldBe("EMAIL_SENDER_NONE");
    }

    [Fact]
    public async Task A_test_goes_through_the_sender_as_it_is_saved()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        (Guid connection, string name) = await ConnectionAsync(admin);
        try
        {
            await SaveAsync(admin, "onboarding", new { ConnectionId = connection, FromName = "GGGS Onboarding", Signature = "Welcome aboard!" });
            string address = Address();
            JsonElement tested = await OkAsync(await admin.PostAsJsonAsync("/api/v1/notifications/senders/onboarding/test", new { To = address }, Ct));
            tested.GetProperty("succeeded").GetBoolean().ShouldBeTrue();
            tested.GetProperty("message").GetString()!.ShouldContain(name);

            IReadOnlyDictionary<string, string> sent = factory.Recorder.Sent.Single(message => message.GetValueOrDefault("to") == address);
            sent["subject"].ShouldContain("Onboarding");
            sent["fromName"].ShouldBe("GGGS Onboarding");
            sent["body"].ShouldEndWith("Welcome aboard!");
        }
        finally
        {
            await RemoveAsync(admin, "onboarding");
        }
    }

    private static string Address() => $"{Guid.NewGuid():N}@senders.example";

    private async Task NotifyAsync(string code, string address, IReadOnlyDictionary<string, string> values)
    {
        await factory.RunAsWorkerAsync(PeopleOsApiFactory.SeededTenantId, async provider =>
        {
            await provider.GetRequiredService<INotifier>().NotifyAsync(new NotificationRequest(code, [new NotificationRecipient(Email: address)], values), Ct);
            await provider.GetRequiredService<AppDbContext>().SaveChangesAsync(Ct);
        });
        await factory.DeliverEventsAsync();
    }

    private async Task<EmailMessage> EmailAsync(string address)
    {
        EmailMessage? email = null;
        await factory.RunAsWorkerAsync(PeopleOsApiFactory.SeededTenantId, async provider =>
            email = await provider.GetRequiredService<AppDbContext>().Set<EmailMessage>().AsNoTracking().SingleAsync(row => row.ToAddress == address, Ct));
        return email!;
    }

    private static async Task SaveAsync(HttpClient admin, string key, object body)
        => await OkAsync(await admin.PutAsJsonAsync($"/api/v1/notifications/senders/{key}", body, Ct));

    private static async Task RemoveAsync(HttpClient admin, string key)
        => await OkAsync(await admin.DeleteAsync($"/api/v1/notifications/senders/{key}", Ct));

    private static async Task<JsonElement> TemplateAsync(HttpClient admin, string code)
        => (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/notifications/templates", Ct))!.Single(row => row.GetProperty("code").GetString() == code);

    private static async Task<HttpResponseMessage> PutTemplateAsync(HttpClient admin, JsonElement template, string? senderKey)
        => await admin.PutAsJsonAsync($"/api/v1/notifications/templates/{template.GetProperty("id").GetGuid()}", new
        {
            Subject = template.GetProperty("subject").GetString(),
            Body = template.GetProperty("body").GetString(),
            SendInApp = template.GetProperty("sendInApp").GetBoolean(),
            SendEmail = template.GetProperty("sendEmail").GetBoolean(),
            SendSms = template.GetProperty("sendSms").GetBoolean(),
            IsActive = template.GetProperty("isActive").GetBoolean(),
            SenderKey = senderKey,
        }, Ct);

    private static async Task UpdateTemplateAsync(HttpClient admin, JsonElement template, string? senderKey)
        => await OkAsync(await PutTemplateAsync(admin, template, senderKey));

    private static async Task<(Guid Id, string Name)> ConnectionAsync(HttpClient admin, string adapter = RecordingEmailAdapter.AdapterKey)
    {
        string name = $"Sender {Guid.NewGuid():N}"[..16];
        JsonElement saved = await OkAsync(await admin.PostAsJsonAsync("/api/v1/integrations/connections", new
        {
            AdapterKey = adapter,
            Name = name,
            Settings = new Dictionary<string, string> { ["fail"] = "no" },
        }, Ct));
        return (saved.GetProperty("id").GetGuid(), name);
    }

    private static async Task<JsonElement> OkAsync(HttpResponseMessage response)
    {
        string text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }
}
