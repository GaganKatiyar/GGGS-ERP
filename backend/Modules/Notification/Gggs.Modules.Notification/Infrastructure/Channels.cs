using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace Gggs.Modules.Notification.Infrastructure;

/// <summary>Where notifications are delivered from, and how.</summary>
public sealed class NotificationOptions
{
    public const string SectionName = "Gggs:Notifications";

    /// <summary>The SPA's public address, for links inside emails.</summary>
    public string AppBaseUrl { get; set; } = "http://localhost:4200";

    /// <summary><c>Log</c> (development: written to the log, never sent) or <c>Smtp</c>.</summary>
    public string EmailProvider { get; set; } = "Log";

    public string FromAddress { get; set; } = "no-reply@nutsandbolts.local";

    public string FromName { get; set; } = "Nuts and Bolts";

    public SmtpSettings Smtp { get; set; } = new();
}

public sealed class SmtpSettings
{
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 587;

    public bool EnableSsl { get; set; } = true;

    public string? UserName { get; set; }

    public string? Password { get; set; }
}

/// <summary>A message on its way out. A sensitive one holds a secret, so nothing may write its body anywhere.</summary>
internal sealed record OutboundMessage(string To, string Subject, string Body, bool Sensitive = false);

/// <summary>
/// One way of reaching people (spec §108). Email ships now; SMS, WhatsApp and push are further
/// implementations of this port, chosen per template channel, with nothing above it changing.
/// </summary>
internal interface INotificationChannel
{
    string Name { get; }

    Task SendAsync(OutboundMessage message, CancellationToken cancellationToken);
}

/// <summary>Development email: written to the log - links included, so invitations can be followed - and never sent.</summary>
internal sealed class LoggingEmailChannel(ILogger<LoggingEmailChannel> logger) : INotificationChannel
{
    public string Name => "email:log";

    public Task SendAsync(OutboundMessage message, CancellationToken cancellationToken)
    {
        if (message.Sensitive)
        {
            logger.LogInformation("Email (not sent; EmailProvider=Log) to {To}: its content is sensitive and is not logged.", message.To);
            return Task.CompletedTask;
        }

        logger.LogInformation("Email (not sent; EmailProvider=Log) to {To}: {Subject}\n{Body}", message.To, message.Subject, message.Body);
        return Task.CompletedTask;
    }
}

internal sealed class SmtpEmailChannel(IOptions<NotificationOptions> options) : INotificationChannel
{
    public string Name => "email:smtp";

    public async Task SendAsync(OutboundMessage message, CancellationToken cancellationToken)
    {
        NotificationOptions settings = options.Value;

        using var client = new SmtpClient(settings.Smtp.Host, settings.Smtp.Port)
        {
            EnableSsl = settings.Smtp.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
        };

        if (!string.IsNullOrWhiteSpace(settings.Smtp.UserName))
        {
            client.Credentials = new NetworkCredential(settings.Smtp.UserName, settings.Smtp.Password);
        }

        using var mail = new MailMessage
        {
            From = new MailAddress(settings.FromAddress, settings.FromName),
            Subject = message.Subject,
            Body = message.Body,
            IsBodyHtml = false,
        };
        mail.To.Add(message.To);

        await client.SendMailAsync(mail, cancellationToken);
    }
}
