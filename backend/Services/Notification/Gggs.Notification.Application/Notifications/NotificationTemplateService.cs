using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Notification.Domain.Notifications;

namespace Gggs.Notification.Application.Notifications;

/// <summary>Administering a tenant's own notification wording.</summary>
public sealed class NotificationTemplateService(INotificationTemplateRepository templates, IUnitOfWork unitOfWork)
{
    public async Task<NotificationTemplate> CreateAsync(
        string eventCode,
        NotificationChannel channel,
        string locale,
        string? subject,
        string bodyTemplate,
        CancellationToken cancellationToken = default)
    {
        NotificationTemplate? existing = await templates.GetActiveAsync(eventCode, channel, cancellationToken);
        if (existing is not null)
        {
            throw new DomainRuleException(
                "NOTIFICATION_TEMPLATE_ALREADY_ACTIVE",
                $"An active {channel} template for '{eventCode}' already exists; deactivate it first.");
        }

        NotificationTemplate template = NotificationTemplate.Create(eventCode, channel, locale, subject, bodyTemplate);
        templates.Add(template);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return template;
    }

    public async Task<NotificationTemplate> UpdateContentAsync(
        Guid id, string? subject, string bodyTemplate, CancellationToken cancellationToken = default)
    {
        NotificationTemplate template = await templates.GetAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(NotificationTemplate), id);

        template.UpdateContent(subject, bodyTemplate);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return template;
    }

    public async Task<NotificationTemplate> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default)
    {
        NotificationTemplate template = await templates.GetAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(NotificationTemplate), id);

        if (isActive)
        {
            template.Activate();
        }
        else
        {
            template.Deactivate();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return template;
    }
}
