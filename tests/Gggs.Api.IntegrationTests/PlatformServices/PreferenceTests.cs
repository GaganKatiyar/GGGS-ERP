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

/// <summary>My preferences: which notifications reach someone by email or in the app, and their time zone.</summary>
[Collection(nameof(ApiCollection))]
public sealed class PreferenceTests(PeopleOsApiFactory factory)
{
    private const string TaskAssigned = "workflow.task-assigned";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SwitchingOffTheEmailForANotification_StillLeavesItInTheApp()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        await using ActiveDefinition definition = await DefinitionAsync(admin, [Stage("Manager", [Rule("ReportingManager")])]);
        using HttpClient manager = await PersonAsync(factory, org.Manager);

        JsonElement before = await PreferenceAsync(manager, TaskAssigned);
        before.GetProperty("emailEnabled").GetBoolean().ShouldBeTrue("no choice made yet means the template's channels");
        before.GetProperty("isMandatory").GetBoolean().ShouldBeFalse();

        (await manager.PutAsJsonAsync("/api/v1/me/notification-preferences", new[] { new { Code = TaskAssigned, EmailEnabled = false, InAppEnabled = true } }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await PreferenceAsync(manager, TaskAssigned)).GetProperty("emailEnabled").GetBoolean().ShouldBeFalse();

        await StartAsync(factory, org.Requester);
        await factory.DeliverEventsAsync();

        JsonElement inbox = (await manager.GetFromJsonAsync<JsonElement>("/api/v1/notifications", Ct))!;
        inbox.GetProperty("items").EnumerateArray().ShouldContain(item => item.GetProperty("eventCode").GetString() == TaskAssigned);

        string managerEmail = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/employees/{org.Manager}", Ct)).GetProperty("officialEmail").GetString()!;
        await factory.RunAsWorkerAsync(PeopleOsApiFactory.SeededTenantId, async services =>
        {
            bool emailed = await services.GetRequiredService<AppDbContext>().Set<EmailMessage>()
                .AnyAsync(message => message.ToAddress == managerEmail && message.EventCode == TaskAssigned, Ct);
            emailed.ShouldBeFalse();
        });
    }

    [Fact]
    public async Task AMandatoryNotification_CannotBeSwitchedOff()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        using HttpClient person = await PersonAsync(factory, org.Requester);

        JsonElement invited = await PreferenceAsync(person, "identity.user-invited");
        invited.GetProperty("isMandatory").GetBoolean().ShouldBeTrue();

        (await person.PutAsJsonAsync("/api/v1/me/notification-preferences", new[] { new { Code = "identity.user-invited", EmailEnabled = false, InAppEnabled = false } }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await PreferenceAsync(person, "identity.user-invited")).GetProperty("emailEnabled").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task ATimeZone_IsSaved_AndOneTheServerDoesNotKnowIsRefused()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        using HttpClient person = await PersonAsync(factory, org.Requester);

        HttpResponseMessage saved = await person.PutAsJsonAsync("/api/v1/me/preferences", new { TimeZone = "Asia/Kolkata" }, Ct);
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(Ct));
        (await person.GetFromJsonAsync<JsonElement>("/api/v1/me/preferences", Ct)).GetProperty("timeZone").GetString().ShouldBe("Asia/Kolkata");

        HttpResponseMessage refused = await person.PutAsJsonAsync("/api/v1/me/preferences", new { TimeZone = "Mars/Olympus_Mons" }, Ct);
        refused.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CoreHrApi.ErrorCodeAsync(refused)).ShouldBe("TIME_ZONE_UNKNOWN");
    }

    private static async Task<JsonElement> PreferenceAsync(HttpClient client, string code)
        => (await client.GetFromJsonAsync<JsonElement[]>("/api/v1/me/notification-preferences", Ct))!.Single(row => row.GetProperty("code").GetString() == code);
}
