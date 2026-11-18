using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Text.Json;
using System.Text.Json.Nodes;
using Gggs.BuildingBlocks.Web;
using Gggs.Modules.Insight.Contracts;

namespace Gggs.Modules.Studio.Application;

/// <summary>What every email connector can do, so the app's own email and automations can both use it.</summary>
internal static class EmailOperation
{
    public const string Key = "send";

    public static IReadOnlyList<IntegrationOperation> All { get; } =
    [
        new(Key, "Send an email", "Sends a plain-text email.", [new("to", "To", Hint: "An address, or a field such as the employee's work email"), new("subject", "Subject"), new("body", "Message")]),
    ];
}

/// <summary>
/// Optional parameters the app's own email passes beside to, subject and body: the module's sender and an
/// HTML version. Automations do not offer them; an adapter uses each one it is given and its settings otherwise.
/// </summary>
internal static class EmailParameters
{
    public const string Html = "html";
    public const string FromName = "fromName";
    public const string FromAddress = "fromAddress";
    public const string ReplyTo = "replyTo";
    public const string Bcc = "bcc";

    public static string? Get(IReadOnlyDictionary<string, string> parameters, string key)
        => parameters.GetValueOrDefault(key) is { Length: > 0 } value && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;
}

/// <summary>One email as an adapter sends it.</summary>
internal sealed record MailParts(string To, string Subject, string Body, string? Html = null, string? FromName = null, string? FromAddress = null, string? ReplyTo = null, string? Bcc = null)
{
    public static MailParts From(IReadOnlyDictionary<string, string> parameters) => new(
        parameters.GetValueOrDefault("to") ?? string.Empty,
        parameters.GetValueOrDefault("subject") ?? string.Empty,
        parameters.GetValueOrDefault("body") ?? string.Empty,
        EmailParameters.Get(parameters, EmailParameters.Html),
        EmailParameters.Get(parameters, EmailParameters.FromName),
        EmailParameters.Get(parameters, EmailParameters.FromAddress),
        EmailParameters.Get(parameters, EmailParameters.ReplyTo),
        EmailParameters.Get(parameters, EmailParameters.Bcc));
}

/// <summary>The organisation's own mail server. STARTTLS is required: a password never crosses the network in the clear.</summary>
internal sealed class SmtpAdapter : IIntegrationAdapter
{
    private static readonly TimeSpan SendLimit = TimeSpan.FromSeconds(30);

    public string Key => "smtp";

    public string Name => "SMTP mail server";

    public string Category => "Email";

    public string Description => "Sends email from your own domain through your organisation's mail server.";

    public bool Automatable => true;

    public string? Channel => IntegrationChannels.Email;

    public IReadOnlyList<IntegrationField> Fields { get; } =
    [
        new("host", "Server", Hint: "smtp.example.com"),
        new("port", "Port", Required: false, Hint: "587, the STARTTLS port, unless your server says otherwise"),
        new("userName", "User name", Required: false),
        new("password", "Password", Secret: true, Required: false),
        new("fromAddress", "Send from", Hint: "people@example.com"),
        new("fromName", "Sender name", Required: false, Hint: "e.g. Acme People Team"),
    ];

    public IReadOnlyList<IntegrationOperation> Operations => EmailOperation.All;

    /// <summary>The only way to know a mail server takes these credentials is for it to accept a message: one goes to the sender's own address.</summary>
    public Task<IntegrationResult> TestAsync(IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken = default)
        => SendAsync(settings, new MailParts(settings.GetValueOrDefault("fromAddress") ?? string.Empty, "Nuts and Bolts connection test", "Nuts and Bolts can send email through this server."), cancellationToken);

    public Task<IntegrationResult> RunAsync(string operation, IReadOnlyDictionary<string, string> settings, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken = default)
        => SendAsync(settings, MailParts.From(parameters), cancellationToken);

    /// <summary>The message as SMTP sends it: plain text, with the HTML version beside it when there is one.</summary>
    public static MailMessage Message(IReadOnlyDictionary<string, string> settings, MailParts parts)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(parts);
        var from = new MailAddress(parts.FromAddress ?? settings.GetValueOrDefault("fromAddress") ?? string.Empty, parts.FromName ?? settings.GetValueOrDefault("fromName"));
        var mail = new MailMessage { From = from, Subject = parts.Subject, Body = parts.Body, IsBodyHtml = false };
        try
        {
            mail.To.Add(parts.To);
            if (parts.ReplyTo is not null)
            {
                mail.ReplyToList.Add(parts.ReplyTo);
            }

            if (parts.Bcc is not null)
            {
                mail.Bcc.Add(parts.Bcc);
            }

            if (parts.Html is not null)
            {
                mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(parts.Html, System.Text.Encoding.UTF8, System.Net.Mime.MediaTypeNames.Text.Html));
            }

            return mail;
        }
        catch
        {
            mail.Dispose();
            throw;
        }
    }

    private static async Task<IntegrationResult> SendAsync(IReadOnlyDictionary<string, string> settings, MailParts parts, CancellationToken cancellationToken)
    {
        string portText = settings.GetValueOrDefault("port") ?? string.Empty;
        int port = 587;
        if (portText.Length > 0 && !int.TryParse(portText, out port))
        {
            return new IntegrationResult(false, "The port must be a number.");
        }

        // SmtpClient speaks STARTTLS only: on the implicit-TLS port it would wait out the whole limit in silence.
        if (port == 465)
        {
            return new IntegrationResult(false, "Port 465 is not supported. Use your server's STARTTLS port, usually 587.");
        }

        // SmtpClient resolves the name again when it connects, so this does not stop a name that changes in between; it stops the plain mistake and the plain probe.
        if (await OutboundUrlGuard.CheckHostAsync(settings.GetValueOrDefault("host"), cancellationToken) is { } refused)
        {
            return new IntegrationResult(false, refused);
        }

        try
        {
            using var client = new SmtpClient(settings["host"].Trim(), port) { EnableSsl = true, DeliveryMethod = SmtpDeliveryMethod.Network };
            if (settings.GetValueOrDefault("userName") is { Length: > 0 } userName)
            {
                client.Credentials = new NetworkCredential(userName, settings.GetValueOrDefault("password"));
            }

            using MailMessage mail = Message(settings, parts);

            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(SendLimit);
            await client.SendMailAsync(mail, limit.Token);
            return new IntegrationResult(true, "Sent.", Records: 1);
        }
        catch (Exception exception) when (exception is SmtpException or FormatException or ArgumentException or InvalidOperationException)
        {
            return new IntegrationResult(false, exception.Message);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new IntegrationResult(false, "The mail server did not answer in time.");
        }
    }
}

/// <summary>SendGrid's Mail Send API.</summary>
internal sealed class SendGridAdapter(IHttpClientFactory http) : IIntegrationAdapter
{
    public string Key => "sendgrid";

    public string Name => "SendGrid";

    public string Category => "Email";

    public string Description => "Sends email from your own domain through your SendGrid account.";

    public bool Automatable => true;

    public string? Channel => IntegrationChannels.Email;

    public IReadOnlyList<IntegrationField> Fields { get; } =
    [
        new("apiKey", "API key", Secret: true, Hint: "With the Mail Send permission"),
        new("fromAddress", "Send from", Hint: "A verified sender in SendGrid"),
        new("fromName", "Sender name", Required: false),
    ];

    public IReadOnlyList<IntegrationOperation> Operations => EmailOperation.All;

    public static JsonObject Message(string fromAddress, string? fromName, string to, string subject, string body, string? html = null, string? replyTo = null, string? bcc = null)
    {
        var from = new JsonObject { ["email"] = fromAddress };
        if (!string.IsNullOrWhiteSpace(fromName))
        {
            from["name"] = fromName;
        }

        var personalization = new JsonObject { ["to"] = new JsonArray { new JsonObject { ["email"] = to } } };
        if (!string.IsNullOrWhiteSpace(bcc))
        {
            personalization["bcc"] = new JsonArray { new JsonObject { ["email"] = bcc } };
        }

        var content = new JsonArray { new JsonObject { ["type"] = "text/plain", ["value"] = body } };
        if (!string.IsNullOrWhiteSpace(html))
        {
            content.Add(new JsonObject { ["type"] = "text/html", ["value"] = html });
        }

        var message = new JsonObject
        {
            ["personalizations"] = new JsonArray { personalization },
            ["from"] = from,
            ["subject"] = subject,
            ["content"] = content,
        };
        if (!string.IsNullOrWhiteSpace(replyTo))
        {
            message["reply_to"] = new JsonObject { ["email"] = replyTo };
        }

        return message;
    }

    /// <summary>Asks what the key may do, which checks it without sending anything.</summary>
    public async Task<IntegrationResult> TestAsync(IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.sendgrid.com/v3/scopes");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.GetValueOrDefault("apiKey"));
        using HttpResponseMessage response = await http.CreateClient(StudioHttp.ClientName).SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode ? new IntegrationResult(true, "SendGrid accepted the API key.") : new IntegrationResult(false, await StudioHttp.RefusalAsync(response, Name, cancellationToken));
    }

    public async Task<IntegrationResult> RunAsync(string operation, IReadOnlyDictionary<string, string> settings, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken = default)
    {
        MailParts parts = MailParts.From(parameters);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.sendgrid.com/v3/mail/send")
        {
            Content = JsonContent.Create(Message(parts.FromAddress ?? settings.GetValueOrDefault("fromAddress") ?? string.Empty, parts.FromName ?? settings.GetValueOrDefault("fromName"), parts.To, parts.Subject, parts.Body, parts.Html, parts.ReplyTo, parts.Bcc)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.GetValueOrDefault("apiKey"));
        using HttpResponseMessage response = await http.CreateClient(StudioHttp.ClientName).SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode ? new IntegrationResult(true, "Sent.", Records: 1) : new IntegrationResult(false, await StudioHttp.RefusalAsync(response, Name, cancellationToken));
    }
}

/// <summary>Microsoft 365: sends as one mailbox through Microsoft Graph, with an app registration's credentials.</summary>
internal sealed class Microsoft365MailAdapter(IHttpClientFactory http) : IIntegrationAdapter
{
    public string Key => "microsoft-365-mail";

    public string Name => "Microsoft 365 email";

    public string Category => "Email";

    public string Description => "Sends email from a Microsoft 365 mailbox through Microsoft Graph. The app registration needs the Mail.Send application permission.";

    public bool Automatable => true;

    public string? Channel => IntegrationChannels.Email;

    public IReadOnlyList<IntegrationField> Fields { get; } =
    [
        new("tenantId", "Directory (tenant) ID"),
        new("clientId", "Application (client) ID"),
        new("clientSecret", "Client secret", Secret: true),
        new("mailbox", "Send from mailbox", Hint: "people@example.com"),
    ];

    public IReadOnlyList<IntegrationOperation> Operations => EmailOperation.All;

    /// <summary>Graph takes one body: the HTML version when there is one, the text otherwise.</summary>
    public static JsonObject Message(string to, string subject, string body, string? html = null, string? replyTo = null, string? bcc = null)
    {
        var message = new JsonObject
        {
            ["subject"] = subject,
            ["body"] = string.IsNullOrWhiteSpace(html)
                ? new JsonObject { ["contentType"] = "Text", ["content"] = body }
                : new JsonObject { ["contentType"] = "HTML", ["content"] = html },
            ["toRecipients"] = Recipients(to),
        };
        if (!string.IsNullOrWhiteSpace(replyTo))
        {
            message["replyTo"] = Recipients(replyTo);
        }

        if (!string.IsNullOrWhiteSpace(bcc))
        {
            message["bccRecipients"] = Recipients(bcc);
        }

        return new JsonObject { ["message"] = message, ["saveToSentItems"] = false };
    }

    /// <summary>Signing in proves the credentials but not the Mail.Send permission; only a send can, which is what the Sending page's test is for.</summary>
    public async Task<IntegrationResult> TestAsync(IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken = default)
        => await TokenAsync(settings, cancellationToken) is null
            ? new IntegrationResult(false, "Microsoft refused the client credentials.")
            : new IntegrationResult(true, "Signed in to Microsoft Graph. Send a test from Sending to confirm the app may send mail.");

    public async Task<IntegrationResult> RunAsync(string operation, IReadOnlyDictionary<string, string> settings, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken = default)
    {
        string? token = await TokenAsync(settings, cancellationToken);
        if (token is null)
        {
            return new IntegrationResult(false, "Microsoft refused the client credentials.");
        }

        // A module's own address sends as that mailbox, which the app registration must be allowed to use.
        MailParts parts = MailParts.From(parameters);
        string mailbox = Uri.EscapeDataString(parts.FromAddress ?? settings.GetValueOrDefault("mailbox") ?? string.Empty);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://graph.microsoft.com/v1.0/users/{mailbox}/sendMail")
        {
            Content = JsonContent.Create(Message(parts.To, parts.Subject, parts.Body, parts.Html, parts.ReplyTo, parts.Bcc)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using HttpResponseMessage response = await http.CreateClient(StudioHttp.ClientName).SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode ? new IntegrationResult(true, "Sent.", Records: 1) : new IntegrationResult(false, await StudioHttp.RefusalAsync(response, Name, cancellationToken));
    }

    private async Task<string?> TokenAsync(IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken)
    {
        string tenant = Uri.EscapeDataString(settings.GetValueOrDefault("tenantId") ?? string.Empty);
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = settings.GetValueOrDefault("clientId") ?? string.Empty,
            ["client_secret"] = settings.GetValueOrDefault("clientSecret") ?? string.Empty,
            ["scope"] = "https://graph.microsoft.com/.default",
            ["grant_type"] = "client_credentials",
        });
        using HttpResponseMessage response = await http.CreateClient(StudioHttp.ClientName).PostAsync(new Uri($"https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token"), content, cancellationToken);
        return response.IsSuccessStatusCode && (await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).TryGetProperty("access_token", out JsonElement token) ? token.GetString() : null;
    }

    private static JsonArray Recipients(string address) => [new JsonObject { ["emailAddress"] = new JsonObject { ["address"] = address } }];
}
