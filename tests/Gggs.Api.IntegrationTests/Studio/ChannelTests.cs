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

/// <summary>The organisation's own email and text messages: which connection sends them, and when one is sent at all.</summary>
[Collection(nameof(ApiCollection))]
public sealed class ChannelTests(PeopleOsApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Email_GoesThroughTheChosenConnection_AndBackToTheSystemWhenItIsSwitchedOff()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        (Guid connection, string name) = await ConnectionAsync(admin, RecordingEmailAdapter.AdapterKey);
        try
        {
            await SetChannelAsync(admin, "email", connection);
            string address = Address();
            await NotifyByEmailAsync(address);

            IReadOnlyDictionary<string, string> sent = factory.Recorder.Sent.Single(message => message.GetValueOrDefault("to") == address);
            sent["subject"].ShouldNotBeNullOrWhiteSpace();
            sent["body"].ShouldNotBeNullOrWhiteSpace();
            (await EmailAsync(address)).Status.ShouldBe(EmailStatus.Sent);

            // Switched off, the connection is out of the way: mail goes the system's way and is not lost.
            await OkAsync(await admin.PutAsJsonAsync($"/api/v1/integrations/connections/{connection}", new { AdapterKey = RecordingEmailAdapter.AdapterKey, Name = name, Settings = new Dictionary<string, string>(), IsActive = false }, Ct));
            string second = Address();
            await NotifyByEmailAsync(second);
            factory.Recorder.Sent.ShouldNotContain(message => message.GetValueOrDefault("to") == second);
            (await EmailAsync(second)).Status.ShouldBe(EmailStatus.Sent);
        }
        finally
        {
            await SetChannelAsync(admin, "email", null);
        }
    }

    [Fact]
    public async Task AFailingConnection_MarksTheEmailFailed_WithoutFallingBack()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        (Guid connection, _) = await ConnectionAsync(admin, RecordingEmailAdapter.AdapterKey, fail: true);
        try
        {
            await SetChannelAsync(admin, "email", connection);
            string address = Address();
            await NotifyByEmailAsync(address);

            EmailMessage email = await EmailAsync(address);
            email.Status.ShouldBe(EmailStatus.Failed);
            email.LastError!.ShouldContain("told to fail");
        }
        finally
        {
            await SetChannelAsync(admin, "email", null);
        }
    }

    [Fact]
    public async Task AChannel_TakesOnlyAConnectionThatCanCarryIt_AndATestSendsForReal()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        (Guid email, _) = await ConnectionAsync(admin, RecordingEmailAdapter.AdapterKey);
        (Guid sms, _) = await ConnectionAsync(admin, RecordingSmsAdapter.AdapterKey);
        try
        {
            (await CoreHrApi.ErrorCodeAsync(await admin.PutAsJsonAsync("/api/v1/studio/channels/email", new { ConnectionId = sms }, Ct))).ShouldBe("CHANNEL_CONNECTION");
            (await admin.PutAsJsonAsync("/api/v1/studio/channels/carrier-pigeon", new { ConnectionId = email }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await CoreHrApi.ErrorCodeAsync(await admin.PostAsJsonAsync("/api/v1/studio/channels/sms/test", new { To = "+14155550100" }, Ct))).ShouldBe("CHANNEL_NONE");

            await SetChannelAsync(admin, "sms", sms);
            JsonElement[] channels = (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/studio/channels", Ct))!;
            JsonElement channel = channels.Single(row => row.GetProperty("channel").GetString() == "sms");
            channel.GetProperty("connectionId").GetGuid().ShouldBe(sms);
            channel.GetProperty("connections").EnumerateArray().ShouldContain(row => row.GetProperty("id").GetGuid() == sms);
            channel.GetProperty("connections").EnumerateArray().ShouldNotContain(row => row.GetProperty("id").GetGuid() == email);

            string number = "+1415555" + Random.Shared.Next(1000, 9999);
            JsonElement tested = await OkAsync(await admin.PostAsJsonAsync("/api/v1/studio/channels/sms/test", new { To = number }, Ct));
            tested.GetProperty("succeeded").GetBoolean().ShouldBeTrue();
            factory.Recorder.Sent.ShouldContain(message => message.GetValueOrDefault("to") == number);
        }
        finally
        {
            await SetChannelAsync(admin, "sms", null);
        }
    }

    [Fact]
    public async Task ATextMessage_GoesOnlyWhenEveryConditionHolds()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid department = await CoreHrApi.DepartmentAsync(admin);
        Guid person = await CoreHrApi.HireAsync(admin, department);
        string number = "+9198" + Random.Shared.Next(10000000, 99999999);
        await SetMobileAsync(admin, person, number);
        Guid withoutMobile = await CoreHrApi.HireAsync(admin, department);
        (Guid connection, _) = await ConnectionAsync(admin, RecordingSmsAdapter.AdapterKey);
        const string Code = NotificationTemplates.AssetAssigned;

        try
        {
            // A template sends no text message until someone chooses it.
            await SetChannelAsync(admin, "sms", connection);
            (await TextsAfterNotifyingAsync(Code, person, number)).ShouldBeEmpty();

            // Chosen, but no connection set: nothing is queued that could not go.
            await SetTemplateSmsAsync(admin, Code, true);
            await SetChannelAsync(admin, "sms", null);
            (await TextsAfterNotifyingAsync(Code, person, number)).ShouldBeEmpty();

            // Everything in place, it goes, and the record says so.
            await SetChannelAsync(admin, "sms", connection);
            (await TextsAfterNotifyingAsync(Code, person, number)).ShouldHaveSingleItem().Status.ShouldBe(SmsStatus.Sent);
            factory.Recorder.Sent.ShouldContain(message => message.GetValueOrDefault("to") == number && message.GetValueOrDefault("text")!.Length > 0);

            // Someone with no mobile is simply not texted, and their other notifications still go.
            await NotifyAsync(Code, new NotificationRecipient(withoutMobile));
            await factory.RunAsWorkerAsync(PeopleOsApiFactory.SeededTenantId, async provider =>
                (await provider.GetRequiredService<AppDbContext>().Set<InAppNotification>().CountAsync(row => row.RecipientEmployeeId == withoutMobile, Ct)).ShouldBeGreaterThan(0));

            // Their own choice is respected...
            await SetPreferenceAsync(person, Code, smsEnabled: false);
            (await TextsAfterNotifyingAsync(Code, person, number)).Count.ShouldBe(1, "still only the one sent earlier");

            // ...except for a notice that must reach them.
            await SetTemplateSmsAsync(admin, NotificationTemplates.AbsenceNotice, true);
            await SetPreferenceAsync(person, NotificationTemplates.AbsenceNotice, smsEnabled: false);
            await NotifyAsync(NotificationTemplates.AbsenceNotice, new NotificationRecipient(person));
            (await TextsAsync(number)).Count.ShouldBe(2, "a mandatory notice ignores the choice");
        }
        finally
        {
            await SetTemplateSmsAsync(admin, Code, false);
            await SetTemplateSmsAsync(admin, NotificationTemplates.AbsenceNotice, false);
            await SetChannelAsync(admin, "sms", null);
        }
    }

    [Fact]
    public async Task ChoicesAboutTextMessages_ShowOnlyOnceTheyCanBeSent()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        (Guid connection, _) = await ConnectionAsync(admin, RecordingSmsAdapter.AdapterKey);
        const string Code = NotificationTemplates.LoanUpdate;
        using HttpClient person = await PlatformServicesApi.PersonAsync(factory, await CoreHrApi.HireAsync(admin, await CoreHrApi.DepartmentAsync(admin)));
        try
        {
            await SetTemplateSmsAsync(admin, Code, true);
            (await PreferenceAsync(person, Code)).GetProperty("templateSendsSms").GetBoolean().ShouldBeFalse("no connection is set");

            await SetChannelAsync(admin, "sms", connection);
            JsonElement shown = await PreferenceAsync(person, Code);
            shown.GetProperty("templateSendsSms").GetBoolean().ShouldBeTrue();
            shown.GetProperty("smsEnabled").GetBoolean().ShouldBeTrue();

            (await person.PutAsJsonAsync("/api/v1/me/notification-preferences", new[] { new { Code, EmailEnabled = true, InAppEnabled = true, SmsEnabled = false } }, Ct)).EnsureSuccessStatusCode();
            (await PreferenceAsync(person, Code)).GetProperty("smsEnabled").GetBoolean().ShouldBeFalse();
        }
        finally
        {
            await SetTemplateSmsAsync(admin, Code, false);
            await SetChannelAsync(admin, "sms", null);
        }
    }

    [Fact]
    public async Task ATrigger_OffersTheEmployeesMobile_ForAnAutomationToText()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        string departmentName = "Automation " + CoreHrApi.Unique("D");
        Guid department = await CoreHrApi.CreateAsync(admin, "/api/v1/organizations/departments", new { Code = CoreHrApi.Unique("DEPT"), Name = departmentName });
        (Guid connection, _) = await ConnectionAsync(admin, RecordingSmsAdapter.AdapterKey);
        await OkAsync(await admin.PostAsJsonAsync("/api/v1/studio/automations", new
        {
            Name = "Text a new joiner",
            TriggerCode = "employee.hired",
            Conditions = new[] { new { Field = "department", Operator = "Equals", Value = departmentName } },
            ConnectionId = connection,
            Operation = "send",
            Parameters = new Dictionary<string, string> { ["to"] = "{{employeeMobile}}", ["text"] = "Welcome, {{employeeName}}" },
        }, Ct));

        string number = "+9197" + Random.Shared.Next(10000000, 99999999);
        HttpResponseMessage hired = await admin.PostAsJsonAsync("/api/v1/employees", new
        {
            FirstName = "Test",
            LastName = "Texted",
            MobilePhone = number,
            JoiningDate = "2024-01-15",
            Assignment = new { DepartmentId = department, EmploymentTypeCode = "FULL_TIME", WorkModeCode = "HYBRID", Designation = "Engineer" },
        }, Ct);
        hired.StatusCode.ShouldBe(HttpStatusCode.Created, await hired.Content.ReadAsStringAsync(Ct));
        await factory.DeliverEventsAsync();

        factory.Recorder.Sent.ShouldContain(message => message.GetValueOrDefault("to") == number && message.GetValueOrDefault("text") == "Welcome, Test Texted");

        // The sample fills a test send, and must not reach anyone.
        JsonElement sample = (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/studio/triggers", Ct))!.Single(trigger => trigger.GetProperty("code").GetString() == "employee.hired");
        sample.GetProperty("fields").EnumerateArray().Single(field => field.GetProperty("key").GetString() == "employeeMobile").GetProperty("sample").GetString().ShouldBe("+0000000000");
    }

    private static string Address() => $"{Guid.NewGuid():N}@channels.example";

    private async Task NotifyByEmailAsync(string address)
        => await NotifyAsync(NotificationTemplates.UserInvited, new NotificationRecipient(Email: address));

    private async Task NotifyAsync(string code, NotificationRecipient recipient)
    {
        await factory.RunAsWorkerAsync(PeopleOsApiFactory.SeededTenantId, async provider =>
        {
            await provider.GetRequiredService<INotifier>().NotifyAsync(new NotificationRequest(code, [recipient], new Dictionary<string, string>()), Ct);
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

    private async Task<List<SmsMessage>> TextsAfterNotifyingAsync(string code, Guid person, string number)
    {
        await NotifyAsync(code, new NotificationRecipient(person));
        return await TextsAsync(number);
    }

    private async Task<List<SmsMessage>> TextsAsync(string number)
    {
        List<SmsMessage> texts = [];
        await factory.RunAsWorkerAsync(PeopleOsApiFactory.SeededTenantId, async provider =>
            texts = await provider.GetRequiredService<AppDbContext>().Set<SmsMessage>().AsNoTracking().Where(row => row.ToNumber == number).ToListAsync(Ct));
        return texts;
    }

    private async Task SetPreferenceAsync(Guid employee, string code, bool smsEnabled)
        => await factory.RunAsWorkerAsync(PeopleOsApiFactory.SeededTenantId, async provider =>
        {
            AppDbContext database = provider.GetRequiredService<AppDbContext>();
            database.Set<NotificationPreference>().Add(NotificationPreference.Create(Guid.NewGuid(), employee, code, true, true, smsEnabled));
            await database.SaveChangesAsync(Ct);
        });

    private static async Task SetMobileAsync(HttpClient admin, Guid employee, string number)
        => (await admin.PutAsJsonAsync($"/api/v1/employees/{employee}/contact", new { MobilePhone = number }, Ct)).EnsureSuccessStatusCode();

    private static async Task SetTemplateSmsAsync(HttpClient admin, string code, bool on)
    {
        JsonElement template = (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/notifications/templates", Ct))!.Single(row => row.GetProperty("code").GetString() == code);
        await OkAsync(await admin.PutAsJsonAsync($"/api/v1/notifications/templates/{template.GetProperty("id").GetGuid()}", new
        {
            Subject = template.GetProperty("subject").GetString(),
            Body = template.GetProperty("body").GetString(),
            SendInApp = template.GetProperty("sendInApp").GetBoolean(),
            SendEmail = template.GetProperty("sendEmail").GetBoolean(),
            SendSms = on,
            IsActive = true,
        }, Ct));
    }

    private static async Task<JsonElement> PreferenceAsync(HttpClient person, string code)
        => (await person.GetFromJsonAsync<JsonElement[]>("/api/v1/me/notification-preferences", Ct))!.Single(row => row.GetProperty("code").GetString() == code);

    private static async Task SetChannelAsync(HttpClient admin, string channel, Guid? connection)
        => await OkAsync(await admin.PutAsJsonAsync($"/api/v1/studio/channels/{channel}", new { ConnectionId = connection }, Ct));

    private static async Task<(Guid Id, string Name)> ConnectionAsync(HttpClient admin, string adapter, bool fail = false)
    {
        string name = $"Channel {Guid.NewGuid():N}"[..16];
        JsonElement saved = await OkAsync(await admin.PostAsJsonAsync("/api/v1/integrations/connections", new
        {
            AdapterKey = adapter,
            Name = name,
            Settings = new Dictionary<string, string> { ["fail"] = fail ? "yes" : "no" },
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
