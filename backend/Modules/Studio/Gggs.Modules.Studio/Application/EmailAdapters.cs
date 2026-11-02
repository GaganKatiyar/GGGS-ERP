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
        => SendAsync(settings, settings.GetValueOrDefault("fromAddress") ?? string.Empty, "Nuts and Bolts connection test", "Nuts and Bolts can send email through this server.", cancellationToken);

    public Task<IntegrationResult> RunAsync(string operation, IReadOnlyDictionary<string, string> settings, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken = default)
        => SendAsync(settings, parameters.GetValueOrDefault("to") ?? string.Empty, parameters.GetValueOrDefault("subject") ?? string.Empty, parameters.GetValueOrDefault("body") ?? string.Empty, cancellationToken);

    private static async Task<IntegrationResult> SendAsync(IReadOnlyDictionary<string, string> settings, string to, string subject, string body, CancellationToken cancellationToken)
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

            using var mail = new MailMessage { From = new MailAddress(settings.GetValueOrDefault("fromAddress") ?? string.Empty, settings.GetValueOrDefault("fromName")), Subject = subject, Body = body, IsBodyHtml = false };
            mail.To.Add(to);

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

    public static JsonObject Message(string fromAddress, string? fromName, string to, string subject, string body)
    {
        var from = new JsonObject { ["email"] = fromAddress };
        if (!string.IsNullOrWhiteSpace(fromName))
        {
            from["name"] = fromName;
        }

        return new JsonObject
        {
            ["personalizations"] = new JsonArray { new JsonObject { ["to"] = new JsonArray { new JsonObject { ["email"] = to } } } },
            ["from"] = from,
            ["subject"] = subject,
            ["content"] = new JsonArray { new JsonObject { ["type"] = "text/plain", ["value"] = body } },
        };
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
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.sendgrid.com/v3/mail/send")
        {
            Content = JsonContent.Create(Message(settings.GetValueOrDefault("fromAddress") ?? string.Empty, settings.GetValueOrDefault("fromName"), parameters.GetValueOrDefault("to") ?? string.Empty, parameters.GetValueOrDefault("subject") ?? string.Empty, parameters.GetValueOrDefault("body") ?? string.Empty)),
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

    public static JsonObject Message(string to, string subject, string body) => new()
    {
        ["message"] = new JsonObject
        {
            ["subject"] = subject,
            ["body"] = new JsonObject { ["contentType"] = "Text", ["content"] = body },
            ["toRecipients"] = new JsonArray { new JsonObject { ["emailAddress"] = new JsonObject { ["address"] = to } } },
        },
        ["saveToSentItems"] = false,
    };

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

        string mailbox = Uri.EscapeDataString(settings.GetValueOrDefault("mailbox") ?? string.Empty);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://graph.microsoft.com/v1.0/users/{mailbox}/sendMail")
        {
            Content = JsonContent.Create(Message(parameters.GetValueOrDefault("to") ?? string.Empty, parameters.GetValueOrDefault("subject") ?? string.Empty, parameters.GetValueOrDefault("body") ?? string.Empty)),
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
}
