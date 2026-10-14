using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gggs.Data;
using Gggs.Modules.Notification.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using static Gggs.Api.IntegrationTests.PlatformServicesApi;

namespace Gggs.Api.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class NotificationTests(PeopleOsApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAssignedTask_ReachesTheApproversInbox_AndTheirEmail()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        await using ActiveDefinition definition = await DefinitionAsync(admin, [Stage("Manager", [Rule("ReportingManager")])]);

        await StartAsync(factory, org.Requester);
        await factory.DeliverEventsAsync();

        using HttpClient manager = await PersonAsync(factory, org.Manager);
        JsonElement page = (await manager.GetFromJsonAsync<JsonElement>("/api/v1/notifications", Ct))!;
        JsonElement notification = page.GetProperty("items").EnumerateArray()
            .First(item => item.GetProperty("eventCode").GetString() == "workflow.task-assigned");
        notification.GetProperty("isRead").GetBoolean().ShouldBeFalse();
        notification.GetProperty("link").GetString().ShouldBe("/approvals");

        int unread = (await manager.GetFromJsonAsync<JsonElement>("/api/v1/notifications/unread-count", Ct)).GetProperty("unreadCount").GetInt32();
        unread.ShouldBeGreaterThan(0);

        (await manager.PostAsync($"/api/v1/notifications/{notification.GetProperty("id").GetGuid()}/read", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await manager.PostAsync("/api/v1/notifications/read-all", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await manager.GetFromJsonAsync<JsonElement>("/api/v1/notifications/unread-count", Ct)).GetProperty("unreadCount").GetInt32().ShouldBe(0);

        // The email went through the channel (the logging one in tests) and is on record as sent.
        await factory.RunAsWorkerAsync(PeopleOsApiFactory.SeededTenantId, async services =>
        {
            string managerEmail = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/employees/{org.Manager}", Ct)).GetProperty("officialEmail").GetString()!;
            EmailMessage email = await services.GetRequiredService<AppDbContext>().Set<EmailMessage>()
                .FirstAsync(message => message.ToAddress == managerEmail && message.EventCode == "workflow.task-assigned", Ct);
            email.Status.ShouldBe(EmailStatus.Sent);
            email.Subject.ShouldStartWith("Approval needed:");
        });
    }

    [Fact]
    public async Task ASwitchedOffTemplate_SendsNothing()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        await using ActiveDefinition definition = await DefinitionAsync(admin, [Stage("Manager", [Rule("ReportingManager")])]);

        JsonElement template = (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/notifications/templates", Ct))!
            .First(row => row.GetProperty("code").GetString() == "workflow.task-assigned");
        Guid templateId = template.GetProperty("id").GetGuid();

        try
        {
            HttpResponseMessage off = await admin.PutAsJsonAsync($"/api/v1/notifications/templates/{templateId}", new
            {
                Subject = template.GetProperty("subject").GetString(),
                Body = template.GetProperty("body").GetString(),
                SendInApp = true,
                SendEmail = true,
                IsActive = false,
            }, Ct);
            off.StatusCode.ShouldBe(HttpStatusCode.OK, await off.Content.ReadAsStringAsync(Ct));

            await StartAsync(factory, org.Requester);
            await factory.DeliverEventsAsync();

            using HttpClient manager = await PersonAsync(factory, org.Manager);
            JsonElement page = (await manager.GetFromJsonAsync<JsonElement>("/api/v1/notifications", Ct))!;
            page.GetProperty("items").EnumerateArray().ShouldNotContain(item => item.GetProperty("eventCode").GetString() == "workflow.task-assigned");
        }
        finally
        {
            await admin.PutAsJsonAsync($"/api/v1/notifications/templates/{templateId}", new
            {
                Subject = template.GetProperty("subject").GetString(),
                Body = template.GetProperty("body").GetString(),
                SendInApp = true,
                SendEmail = true,
                IsActive = true,
            }, Ct);
        }
    }

    [Fact]
    public async Task AnInbox_IsItsOwnersAlone()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        await using ActiveDefinition definition = await DefinitionAsync(admin, [Stage("Manager", [Rule("ReportingManager")])]);

        await StartAsync(factory, org.Requester);
        await factory.DeliverEventsAsync();

        using HttpClient manager = await PersonAsync(factory, org.Manager);
        using HttpClient finance = await PersonAsync(factory, org.Finance);

        Guid managersNotification = (await manager.GetFromJsonAsync<JsonElement>("/api/v1/notifications", Ct))
            .GetProperty("items")[0].GetProperty("id").GetGuid();

        (await finance.PostAsync($"/api/v1/notifications/{managersNotification}/read", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await finance.GetFromJsonAsync<JsonElement>("/api/v1/notifications", Ct)).GetProperty("items").EnumerateArray()
            .ShouldNotContain(item => item.GetProperty("id").GetGuid() == managersNotification);
    }

    [Fact]
    public async Task ChangingTemplates_NeedsTheTemplatePermission()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        using HttpClient person = await PersonAsync(factory, org.Requester);

        (await person.GetAsync("/api/v1/notifications/templates", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
