using Gggs.Data;
using Gggs.Modules.Notification.Contracts;
using Gggs.Modules.Notification.Domain;
using Gggs.Modules.Notification.Infrastructure;
using Gggs.Modules.Platform.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Gggs.Modules.Notification.Application;

/// <summary>
/// Today's email for the platform console: how many went, failed or were only logged, and what the
/// organisation's mail goes through. Counts only; no addresses or subjects leave the tenant.
/// </summary>
internal sealed class NotificationUsageProbe(AppDbContext dbContext, ITenantMessageSender messageSender, IOptions<NotificationOptions> options)
    : ITenantUsageProbe
{
    public async Task ReadAsync(TenantUsageReading reading, DateTime todayStartUtc, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reading);

        Dictionary<EmailStatus, int> today = await dbContext.EmailMessages.AsNoTracking()
            .Where(message => message.CreatedDate >= todayStartUtc)
            .GroupBy(message => message.Status)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Key, row => row.Count, cancellationToken);

        reading.EmailsSentToday = today.GetValueOrDefault(EmailStatus.Sent);
        reading.EmailsFailedToday = today.GetValueOrDefault(EmailStatus.Failed);
        reading.EmailsLoggedToday = today.GetValueOrDefault(EmailStatus.Logged);

        EmailConnections connections = await messageSender.EmailConnectionsAsync(cancellationToken);
        Guid? defaultConnection = await dbContext.EmailSenders.AsNoTracking()
            .Where(sender => sender.ModuleKey == EmailModules.DefaultKey)
            .Select(sender => sender.ConnectionId)
            .FirstOrDefaultAsync(cancellationToken);

        Guid? chosen = defaultConnection ?? connections.RouteConnectionId;
        EmailConnectionOption? connection = connections.Options.FirstOrDefault(option => option.Id == chosen && option.IsActive);
        bool systemDelivers = string.Equals(options.Value.EmailProvider, "Smtp", StringComparison.OrdinalIgnoreCase);

        reading.EmailChannel = connection is not null
            ? $"Own connection: {connection.Name}"
            : systemDelivers ? "Platform mail server" : "Not sending (log only)";
    }
}
