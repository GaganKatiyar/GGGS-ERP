using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Notification.Api.Contracts;
using Gggs.Notification.Application.Notifications;
using Gggs.Notification.Domain.Notifications;
using Gggs.Notification.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Notification.Api.Endpoints;

/// <summary>Administering a tenant's own notification wording - see architecture.md §14.</summary>
public static class NotificationTemplateEndpoints
{
    public static IEndpointRouteBuilder MapNotificationTemplateEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/notifications/templates").WithTags("Notification Templates");

        group.MapGet("/", ListAsync).WithName("ListNotificationTemplates").RequirePermission(Permissions.Notification.TemplateView);
        group.MapPost("/", CreateAsync).WithName("CreateNotificationTemplate").RequirePermission(Permissions.Notification.TemplateManage);
        group.MapPut("/{id:guid}", UpdateContentAsync)
            .WithName("UpdateNotificationTemplateContent")
            .RequirePermission(Permissions.Notification.TemplateManage);
        group.MapPost("/{id:guid}/activate", ActivateAsync)
            .WithName("ActivateNotificationTemplate")
            .RequirePermission(Permissions.Notification.TemplateManage);
        group.MapPost("/{id:guid}/deactivate", DeactivateAsync)
            .WithName("DeactivateNotificationTemplate")
            .RequirePermission(Permissions.Notification.TemplateManage);

        return endpoints;
    }

    private static async Task<Ok<IReadOnlyList<NotificationTemplateResponse>>> ListAsync(
        NotificationDbContext database, CancellationToken cancellationToken)
    {
        List<NotificationTemplate> templates = await database.Templates.AsNoTracking()
            .OrderBy(x => x.EventCode)
            .ThenBy(x => x.Channel)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok<IReadOnlyList<NotificationTemplateResponse>>([.. templates.Select(ToResponse)]);
    }

    private static async Task<Created<NotificationTemplateResponse>> CreateAsync(
        CreateNotificationTemplateBody body, NotificationTemplateService service, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        NotificationTemplate created = await service.CreateAsync(
            body.EventCode, body.Channel, body.Locale, body.Subject, body.BodyTemplate, cancellationToken);

        NotificationTemplateResponse response = ToResponse(created);
        return TypedResults.Created($"/api/v1/notifications/templates/{created.Id}", response);
    }

    private static async Task<Ok<NotificationTemplateResponse>> UpdateContentAsync(
        Guid id,
        UpdateNotificationTemplateContentBody body,
        NotificationTemplateService service,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        NotificationTemplate updated = await service.UpdateContentAsync(id, body.Subject, body.BodyTemplate, cancellationToken);
        return TypedResults.Ok(ToResponse(updated));
    }

    private static async Task<Ok<NotificationTemplateResponse>> ActivateAsync(
        Guid id, NotificationTemplateService service, CancellationToken cancellationToken)
        => TypedResults.Ok(ToResponse(await service.SetActiveAsync(id, isActive: true, cancellationToken)));

    private static async Task<Ok<NotificationTemplateResponse>> DeactivateAsync(
        Guid id, NotificationTemplateService service, CancellationToken cancellationToken)
        => TypedResults.Ok(ToResponse(await service.SetActiveAsync(id, isActive: false, cancellationToken)));

    private static NotificationTemplateResponse ToResponse(NotificationTemplate template) => new(
        template.Id, template.EventCode, template.Channel, template.Locale,
        template.Subject, template.BodyTemplate, template.IsActive);
}
