using Gggs.BuildingBlocks.Messaging;
using Gggs.Identity.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Gggs.Notification.Application.Notifications;

/// <summary>
/// The first real trigger this service has: turns Identity's invitation into an in-app row (for
/// whoever is watching the new account, which is nobody yet - it has no session) and a "welcome,
/// set your password" email.
/// </summary>
public sealed class UserInvitedConsumer(
    NotificationDispatchService dispatch,
    IConfiguration configuration,
    ILogger<UserInvitedConsumer> logger) : IIntegrationEventConsumer<UserInvitedIntegrationEvent>
{
    public async Task HandleAsync(UserInvitedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        string appBaseUrl = configuration["Gggs:Notification:AppBaseUrl"] ?? "http://localhost:4200";
        string link =
            $"{appBaseUrl}/complete-invitation?tenantCode={Uri.EscapeDataString(integrationEvent.TenantCode)}" +
            $"&userId={integrationEvent.UserId}&token={Uri.EscapeDataString(integrationEvent.InvitationToken)}";

        await dispatch.DispatchAsync(new NotificationRequest(
            integrationEvent.MessageId,
            EventCode: "user.invited",
            integrationEvent.UserId,
            integrationEvent.Email,
            Title: "You're invited to GGGS HRMS",
            TemplateData: new Dictionary<string, object?>
            {
                ["display_name"] = integrationEvent.DisplayName,
                ["tenant_code"] = integrationEvent.TenantCode,
                ["link"] = link,
                ["expires_date"] = integrationEvent.ExpiresDate,
            },
            Link: link),
            cancellationToken);

        logger.LogInformation("Dispatched the invitation notification for user {UserId}.", integrationEvent.UserId);
    }
}
