using System.Net;
using System.Net.Mail;
using Gggs.Notification.Application.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gggs.Notification.Infrastructure;

public sealed class SmtpEmailSenderOptions
{
    public const string SectionName = "Gggs:Notification:Smtp";

    /// <summary>Empty means "no mail server configured" - see <see cref="SmtpEmailSender"/>'s own remarks.</summary>
    public string? Host { get; set; }

    public int Port { get; set; } = 25;

    public string FromAddress { get; set; } = "no-reply@gggshr.local";

    public string FromDisplayName { get; set; } = "GGGS HRMS";

    public string? Username { get; set; }

    public string? Password { get; set; }

    public bool EnableSsl { get; set; }
}

/// <summary>
/// The only <see cref="IEmailSender"/> this slice ships.
/// <para>
/// architecture.md §19 calls for a real provider adapter per deployment profile (Azure
/// Communication Services / SendGrid in the cloud, a tenant's own SMTP on-premise) behind
/// <c>IEmailSender</c>; only the on-premise half exists here, and only the plain-SMTP shape of it -
/// no ACS/SendGrid adapter is built, which is the same kind of documented gap as
/// <c>DocumentScanStatus</c>'s missing scanner.
/// </para>
/// <para>
/// When no host is configured - the state of a fresh checkout, with no mail catcher standing up -
/// this logs the email instead of failing the request that triggered it. A tenant admin invites
/// someone without an SMTP server reachable and the invite still succeeds; the email is simply
/// visible in the log instead of an inbox, exactly the tradeoff <c>tools/DevSeed</c> already makes
/// for local sign-in.
/// </para>
/// </summary>
public sealed class SmtpEmailSender(
    IOptions<SmtpEmailSenderOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task<string?> SendAsync(
        string toAddress, string subject, string body, CancellationToken cancellationToken = default)
    {
        SmtpEmailSenderOptions settings = options.Value;

        if (string.IsNullOrWhiteSpace(settings.Host))
        {
            logger.LogInformation(
                "No SMTP host configured ({SectionName}); logging instead of sending.\nTo: {ToAddress}\nSubject: {Subject}\n{Body}",
                SmtpEmailSenderOptions.SectionName, toAddress, subject, body);
            return null;
        }

        using var message = new MailMessage
        {
            From = new MailAddress(settings.FromAddress, settings.FromDisplayName),
            Subject = subject,
            Body = body,
            IsBodyHtml = false,
        };
        message.To.Add(toAddress);

        using var client = new SmtpClient(settings.Host, settings.Port) { EnableSsl = settings.EnableSsl };

        if (!string.IsNullOrWhiteSpace(settings.Username))
        {
            client.Credentials = new NetworkCredential(settings.Username, settings.Password);
        }

        await client.SendMailAsync(message, cancellationToken);

        // SmtpClient gives no provider message id; the recipient address plus the outer
        // OutboundMessage row is all troubleshooting a plain SMTP send ever has to go on.
        return null;
    }
}
