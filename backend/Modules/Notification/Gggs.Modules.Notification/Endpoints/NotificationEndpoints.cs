using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Data;
using Gggs.Modules.Notification.Domain;
using Gggs.Modules.Notification.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Notification.Endpoints;

public sealed record InAppNotificationItem(Guid Id, string EventCode, string Title, string Body, string? Link, bool IsRead, DateTime CreatedDate);

public sealed record UnreadCountResponse(int UnreadCount);

public sealed record NotificationTemplateItem(Guid Id, string Code, string Name, string Subject, string Body, bool SendInApp, bool SendEmail, bool IsActive);

public sealed record UpdateNotificationTemplateRequest(string Subject, string Body, bool SendInApp, bool SendEmail, bool IsActive);

public sealed record InboxQuery
{
    public int? Page { get; init; }

    public int? PageSize { get; init; }

    public bool? UnreadOnly { get; init; }

    public PageRequest ToPageRequest() => new() { Page = Page ?? 1, PageSize = PageSize ?? PageRequest.DefaultPageSize };
}

/// <summary>
/// The caller's own inbox, and the wording administrators control. An inbox holds what was sent
/// to the caller's employee record or, for accounts without one, to the user.
/// </summary>
internal static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/notifications").WithTags("Notifications");

        group.MapGet("/", ListAsync).WithName("ListMyNotifications").RequirePermission(Permissions.Notification.InboxView);
        group.MapGet("/unread-count", UnreadCountAsync).WithName("CountMyUnreadNotifications").RequirePermission(Permissions.Notification.InboxView);
        group.MapPost("/{id:guid}/read", MarkReadAsync).WithName("MarkNotificationRead").RequirePermission(Permissions.Notification.InboxManage);
        group.MapPost("/read-all", MarkAllReadAsync).WithName("MarkAllNotificationsRead").RequirePermission(Permissions.Notification.InboxManage);

        group.MapGet("/templates", ListTemplatesAsync).WithName("ListNotificationTemplates").RequirePermission(Permissions.Notification.TemplateView);
        group.MapPut("/templates/{id:guid}", UpdateTemplateAsync).WithName("UpdateNotificationTemplate").RequirePermission(Permissions.Notification.TemplateManage);

        return endpoints;
    }

    private static IQueryable<InAppNotification> Mine(AppDbContext database, ICurrentUser currentUser)
    {
        Guid userId = currentUser.UserId;
        Guid? employeeId = currentUser.EmployeeId;
        return database.InAppNotifications.Where(notification =>
            notification.RecipientUserId == userId || (employeeId != null && notification.RecipientEmployeeId == employeeId));
    }

    private static async Task<Ok<PagedResult<InAppNotificationItem>>> ListAsync(
        [AsParameters] InboxQuery query, AppDbContext database, ICurrentUser currentUser, CancellationToken ct)
    {
        IQueryable<InAppNotification> mine = Mine(database, currentUser).AsNoTracking();
        if (query.UnreadOnly == true)
        {
            mine = mine.Where(notification => notification.ReadDate == null);
        }

        PagedResult<InAppNotificationItem> page = await mine
            .OrderByDescending(notification => notification.CreatedDate)
            .ToPagedResultAsync(
                query.ToPageRequest(),
                notification => new InAppNotificationItem(
                    notification.Id, notification.EventCode, notification.Title, notification.Body, notification.Link,
                    notification.ReadDate != null, notification.CreatedDate),
                ct);

        return TypedResults.Ok(page);
    }

    private static async Task<Ok<UnreadCountResponse>> UnreadCountAsync(AppDbContext database, ICurrentUser currentUser, CancellationToken ct)
        => TypedResults.Ok(new UnreadCountResponse(await Mine(database, currentUser).CountAsync(notification => notification.ReadDate == null, ct)));

    private static async Task<NoContent> MarkReadAsync(Guid id, AppDbContext database, ICurrentUser currentUser, TimeProvider time, CancellationToken ct)
    {
        InAppNotification notification = await Mine(database, currentUser).FirstOrDefaultAsync(row => row.Id == id, ct)
            ?? throw new NotFoundException(nameof(InAppNotification), id);

        notification.MarkRead(time.GetUtcNow().UtcDateTime);
        await database.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<NoContent> MarkAllReadAsync(AppDbContext database, ICurrentUser currentUser, TimeProvider time, CancellationToken ct)
    {
        DateTime now = time.GetUtcNow().UtcDateTime;
        List<InAppNotification> unread = await Mine(database, currentUser).Where(notification => notification.ReadDate == null).Take(1000).ToListAsync(ct);
        foreach (InAppNotification notification in unread)
        {
            notification.MarkRead(now);
        }

        await database.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<List<NotificationTemplateItem>>> ListTemplatesAsync(AppDbContext database, CancellationToken ct)
        => TypedResults.Ok(await database.NotificationTemplates
            .AsNoTracking()
            .OrderBy(template => template.Name)
            .Select(template => new NotificationTemplateItem(
                template.Id, template.Code, template.Name, template.Subject, template.Body, template.SendInApp, template.SendEmail, template.IsActive))
            .ToListAsync(ct));

    private static async Task<Ok<NotificationTemplateItem>> UpdateTemplateAsync(
        Guid id, UpdateNotificationTemplateRequest body, AppDbContext database, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        NotificationTemplate template = await database.NotificationTemplates.FirstOrDefaultAsync(row => row.Id == id, ct)
            ?? throw new NotFoundException(nameof(NotificationTemplate), id);

        template.Update(body.Subject, body.Body, body.SendInApp, body.SendEmail, body.IsActive);
        await database.SaveChangesAsync(ct);

        return TypedResults.Ok(new NotificationTemplateItem(
            template.Id, template.Code, template.Name, template.Subject, template.Body, template.SendInApp, template.SendEmail, template.IsActive));
    }
}
