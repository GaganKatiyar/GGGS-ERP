using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Security;
using Gggs.Notification.Api.Contracts;
using Gggs.Notification.Domain.Notifications;
using Gggs.Notification.Infrastructure;
using Shouldly;

namespace Gggs.Notification.IntegrationTests;

/// <summary>
/// <c>/api/v1/notifications*</c> over HTTP.
/// <para>
/// The inbox endpoints seed <see cref="InAppNotification"/> rows directly rather than through the
/// API: nothing in this API ever creates one - only <c>NotificationDispatchService</c>, which runs
/// in the Worker, does - so exercising "my inbox" against real rows means writing them the way the
/// Worker would, through a platform-scoped context.
/// </para>
/// </summary>
[Collection(nameof(NotificationApiCollection))]
public sealed class NotificationEndpointTests(NotificationApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = BuildJsonOptions();

    private static JsonSerializerOptions BuildJsonOptions()
    {
        var options = new JsonSerializerOptions();
        Gggs.BuildingBlocks.Web.JsonConfiguration.Apply(options);
        return options;
    }

    [Fact]
    public async Task CreatingAnInAppTemplate_NeedsNoSubject()
    {
        using HttpClient client = factory.CreateClientFor(Permissions.Notification.TemplateManage);
        string eventCode = $"evt-{Guid.CreateVersion7():N}";

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/notifications/templates",
            new { EventCode = eventCode, Channel = NotificationChannel.InApp, Locale = "en", Subject = (string?)null, BodyTemplate = "Hi {{ name }}" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task CreatingAnEmailTemplate_WithoutASubject_IsRejected()
    {
        using HttpClient client = factory.CreateClientFor(Permissions.Notification.TemplateManage);
        string eventCode = $"evt-{Guid.CreateVersion7():N}";

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/notifications/templates",
            new { EventCode = eventCode, Channel = NotificationChannel.Email, Locale = "en", Subject = (string?)null, BodyTemplate = "Hi {{ name }}" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task CreatingASecondActiveTemplate_ForTheSameEventAndChannel_IsRejected()
    {
        using HttpClient client = factory.CreateClientFor(Permissions.Notification.TemplateManage);
        string eventCode = $"evt-{Guid.CreateVersion7():N}";

        var body = new { EventCode = eventCode, Channel = NotificationChannel.InApp, Locale = "en", Subject = (string?)null, BodyTemplate = "First" };
        (await client.PostAsJsonAsync("/api/v1/notifications/templates", body, TestContext.Current.CancellationToken))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        HttpResponseMessage second = await client.PostAsJsonAsync(
            "/api/v1/notifications/templates", body, TestContext.Current.CancellationToken);

        second.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task CreatingATemplate_WithoutThePermission_IsRefused()
    {
        using HttpClient client = factory.CreateClientFor(Permissions.Notification.TemplateView);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/notifications/templates",
            new { EventCode = "x", Channel = NotificationChannel.InApp, Locale = "en", Subject = (string?)null, BodyTemplate = "Hi" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeactivatingThenActivating_RoundTrips()
    {
        using HttpClient client = factory.CreateClientFor(Permissions.Notification.TemplateManage);
        string eventCode = $"evt-{Guid.CreateVersion7():N}";

        HttpResponseMessage created = await client.PostAsJsonAsync(
            "/api/v1/notifications/templates",
            new { EventCode = eventCode, Channel = NotificationChannel.InApp, Locale = "en", Subject = (string?)null, BodyTemplate = "Hi" },
            TestContext.Current.CancellationToken);
        NotificationTemplateResponse template = (await created.Content.ReadFromJsonAsync<NotificationTemplateResponse>(
            JsonOptions, TestContext.Current.CancellationToken))!;

        HttpResponseMessage deactivated = await client.PostAsync(
            $"/api/v1/notifications/templates/{template.Id}/deactivate", null, TestContext.Current.CancellationToken);
        NotificationTemplateResponse afterDeactivate = (await deactivated.Content.ReadFromJsonAsync<NotificationTemplateResponse>(
            JsonOptions, TestContext.Current.CancellationToken))!;
        afterDeactivate.IsActive.ShouldBeFalse();

        // Deactivating first means a second active template for the same event/channel is allowed now.
        HttpResponseMessage recreated = await client.PostAsJsonAsync(
            "/api/v1/notifications/templates",
            new { EventCode = eventCode, Channel = NotificationChannel.InApp, Locale = "en", Subject = (string?)null, BodyTemplate = "Hi again" },
            TestContext.Current.CancellationToken);
        recreated.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task ListingMyInbox_ReturnsOnlyMyOwnNotifications()
    {
        string eventCode = $"probe-{Guid.CreateVersion7():N}";
        Guid myNotificationId = await SeedInAppNotificationAsync(NotificationApiFactory.CurrentUserId, eventCode);
        await SeedInAppNotificationAsync(Guid.CreateVersion7(), eventCode);

        using HttpClient client = factory.CreateClientFor(Permissions.Notification.InboxView);

        PagedResult<InAppNotificationItem>? page = await client.GetFromJsonAsync<PagedResult<InAppNotificationItem>>(
            "/api/v1/notifications", JsonOptions, TestContext.Current.CancellationToken);

        page.ShouldNotBeNull();
        page.Items.ShouldContain(item => item.Id == myNotificationId);
        page.Items.Count(item => item.EventCode == eventCode).ShouldBe(1);
    }

    [Fact]
    public async Task MarkingSomeoneElsesNotificationRead_ReturnsNotFound()
    {
        Guid otherUsersNotificationId = await SeedInAppNotificationAsync(
            Guid.CreateVersion7(), $"probe-{Guid.CreateVersion7():N}");

        using HttpClient client = factory.CreateClientFor(Permissions.Notification.InboxManage);

        HttpResponseMessage response = await client.PostAsync(
            $"/api/v1/notifications/{otherUsersNotificationId}/read", null, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MarkingMyOwnNotificationRead_Succeeds()
    {
        Guid notificationId = await SeedInAppNotificationAsync(
            NotificationApiFactory.CurrentUserId, $"probe-{Guid.CreateVersion7():N}");

        using HttpClient client = factory.CreateClientFor(Permissions.Notification.InboxManage);

        HttpResponseMessage response = await client.PostAsync(
            $"/api/v1/notifications/{notificationId}/read", null, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task UnreadCount_CountsOnlyMyOwnUnreadNotifications()
    {
        string eventCode = $"probe-{Guid.CreateVersion7():N}";
        await SeedInAppNotificationAsync(NotificationApiFactory.CurrentUserId, eventCode);
        await SeedInAppNotificationAsync(NotificationApiFactory.CurrentUserId, eventCode);
        await SeedInAppNotificationAsync(Guid.CreateVersion7(), eventCode);

        using HttpClient client = factory.CreateClientFor(
            Permissions.Notification.InboxView, Permissions.Notification.InboxManage);

        UnreadCountResponse? before = await client.GetFromJsonAsync<UnreadCountResponse>(
            "/api/v1/notifications/unread-count", JsonOptions, TestContext.Current.CancellationToken);
        before.ShouldNotBeNull();

        await client.PostAsync("/api/v1/notifications/read-all", null, TestContext.Current.CancellationToken);

        UnreadCountResponse? after = await client.GetFromJsonAsync<UnreadCountResponse>(
            "/api/v1/notifications/unread-count", JsonOptions, TestContext.Current.CancellationToken);
        after!.UnreadCount.ShouldBe(0);
    }

    [Fact]
    public async Task ListingMyInbox_WithoutCredentials_IsRefused()
    {
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(
            "/api/v1/notifications", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private async Task<Guid> SeedInAppNotificationAsync(Guid userId, string eventCode)
    {
        NotificationDbContext db = factory.CreateTenantScopedContext(out IDisposable scopeHandle);
        using IDisposable _ = scopeHandle;

        InAppNotification notification = InAppNotification.Create(userId, eventCode, "Title", "Body", null);
        db.InAppNotifications.Add(notification);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        return notification.Id;
    }
}
