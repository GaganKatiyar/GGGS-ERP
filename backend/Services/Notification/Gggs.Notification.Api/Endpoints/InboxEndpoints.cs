using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Notification.Api.Contracts;
using Gggs.Notification.Domain.Notifications;
using Gggs.Notification.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Notification.Api.Endpoints;

/// <summary>
/// A signed-in user's own notification inbox.
/// <para>
/// Every read and write here is scoped to <c>currentUser.UserId</c>, never to a caller-supplied id
/// - there is no route or query parameter that names a user, so there is nothing for the client to
/// get wrong or falsify. Marking someone else's notification read is not a permission check away,
/// it is a query that can never match another user's row.
/// </para>
/// </summary>
public static class InboxEndpoints
{
    public static IEndpointRouteBuilder MapInboxEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/notifications").WithTags("Notifications");

        group.MapGet("/", ListAsync).WithName("ListMyNotifications").RequirePermission(Permissions.Notification.InboxView);
        group.MapGet("/unread-count", UnreadCountAsync)
            .WithName("GetUnreadNotificationCount")
            .RequirePermission(Permissions.Notification.InboxView);

        group.MapPost("/{id:guid}/read", MarkReadAsync)
            .WithName("MarkNotificationRead")
            .RequirePermission(Permissions.Notification.InboxManage);
        group.MapPost("/read-all", MarkAllReadAsync)
            .WithName("MarkAllNotificationsRead")
            .RequirePermission(Permissions.Notification.InboxManage);

        return endpoints;
    }

    private static async Task<Ok<PagedResult<InAppNotificationItem>>> ListAsync(
        [AsParameters] PageQuery page,
        NotificationDbContext database,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        PageRequest request = page.ToPageRequest();
        IQueryable<InAppNotification> query = database.InAppNotifications
            .AsNoTracking()
            .Where(x => x.UserId == currentUser.UserId);

        long? total = request.IncludeTotal ? await query.LongCountAsync(cancellationToken) : null;

        List<InAppNotificationItem> items = await query
            .OrderByDescending(x => x.CreatedDate)
            .Skip(request.Skip)
            .Take(request.PageSize)
            .Select(x => new InAppNotificationItem(x.Id, x.EventCode, x.Title, x.Body, x.Link, x.IsRead, x.CreatedDate))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(PagedResult.From(items, request, total));
    }

    private static async Task<Ok<UnreadCountResponse>> UnreadCountAsync(
        NotificationDbContext database, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        int count = await database.InAppNotifications
            .AsNoTracking()
            .CountAsync(x => x.UserId == currentUser.UserId && !x.IsRead, cancellationToken);

        return TypedResults.Ok(new UnreadCountResponse(count));
    }

    private static async Task<Results<NoContent, NotFound>> MarkReadAsync(
        Guid id,
        NotificationDbContext database,
        ICurrentUser currentUser,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        InAppNotification? notification = await database.InAppNotifications
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == currentUser.UserId, cancellationToken);

        if (notification is null)
        {
            return TypedResults.NotFound();
        }

        notification.MarkRead(timeProvider.GetUtcNow().UtcDateTime);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<NoContent> MarkAllReadAsync(
        NotificationDbContext database,
        ICurrentUser currentUser,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;

        List<InAppNotification> unread = await database.InAppNotifications
            .Where(x => x.UserId == currentUser.UserId && !x.IsRead)
            .ToListAsync(cancellationToken);

        foreach (InAppNotification notification in unread)
        {
            notification.MarkRead(now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }
}
