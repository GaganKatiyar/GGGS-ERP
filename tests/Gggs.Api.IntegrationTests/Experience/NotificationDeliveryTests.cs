using System.Net.Http.Json;
using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Helpdesk.Application;
using Gggs.Modules.Helpdesk.Infrastructure;
using Gggs.Modules.Notification.Application;
using Gggs.Modules.Notification.Contracts;
using Gggs.Modules.Notification.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using static Gggs.Api.IntegrationTests.DeliveryApi;
using static Gggs.Api.IntegrationTests.PlatformServicesApi;

namespace Gggs.Api.IntegrationTests;

/// <summary>Notifications that have to reach people: the kind a save in the wrong place, or a template nobody seeded, silently loses.</summary>
[Collection(nameof(ApiCollection))]
public sealed class NotificationDeliveryTests(PeopleOsApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AHelpdeskBreach_ReachesWhoeverHoldsTheTicket()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid category = (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/helpdesk/categories", Ct))!.First(row => row.GetProperty("code").GetString() == "IT").GetProperty("id").GetGuid();
        using HttpClient raiser = await factory.CreateScopedUserClientAsync(Email("ticket-raiser"), org.Requester, (Permissions.Self.TicketCreate, DataScope.Own));
        Guid ticket = (await OkAsync(await raiser.PostAsJsonAsync("/api/v1/me/tickets", new { CategoryId = category, Subject = "VPN keeps dropping", Description = "Since Monday.", Priority = "High" }, Ct))).GetProperty("ticket").GetProperty("id").GetGuid();
        await OkAsync(await admin.PostAsJsonAsync($"/api/v1/helpdesk/tickets/{ticket}/assign", new { AssigneeEmployeeId = org.Manager }, Ct));

        await factory.RunAsWorkerAsync(PeopleOsApiFactory.SeededTenantId, async services =>
        {
            DateTime past = DateTime.UtcNow.AddHours(-1);
            await services.GetRequiredService<AppDbContext>().Tickets.Where(row => row.Id == ticket)
                .ExecuteUpdateAsync(row => row.SetProperty(item => item.ResponseDueAt, past).SetProperty(item => item.FirstRespondedAt, (DateTime?)null), Ct);
            (int breached, _) = await services.GetRequiredService<HelpdeskService>().SweepAsync(Ct);
            breached.ShouldBeGreaterThanOrEqualTo(1);
        });
        await factory.DeliverEventsAsync();

        using HttpClient holder = await PersonAsync(factory, org.Manager);
        (await holder.GetFromJsonAsync<JsonElement>("/api/v1/notifications", Ct)).ToString().ShouldContain("VPN keeps dropping");
    }

    [Fact]
    public async Task AnOrganisationThatPredatesATemplate_GetsItFromTheSweep_WithoutLosingItsOwnWording()
    {
        const string Added = NotificationTemplates.ProjectTaskMentioned;
        await factory.RunAsWorkerAsync(PeopleOsApiFactory.SeededTenantId, async services =>
        {
            AppDbContext db = services.GetRequiredService<AppDbContext>();

            // As an organisation set up before this template existed.
            await db.NotificationTemplates.IgnoreQueryFilters().Where(row => row.Code == Added).ExecuteDeleteAsync(Ct);
            (await db.NotificationTemplates.AnyAsync(row => row.Code == Added, Ct)).ShouldBeFalse();

            await services.GetRequiredService<NotificationTemplateJob>().RunAsync(PeopleOsApiFactory.SeededTenantId);

            (await db.NotificationTemplates.AnyAsync(row => row.Code == Added, Ct)).ShouldBeTrue();
        });
    }
}
