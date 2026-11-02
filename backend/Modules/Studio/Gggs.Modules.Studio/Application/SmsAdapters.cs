using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Gggs.Modules.Insight.Contracts;

namespace Gggs.Modules.Studio.Application;

/// <summary>What every SMS and WhatsApp connector can do, so notifications and automations can both use it.</summary>
internal static class SmsOperation
{
    public const string Key = "send";

    public static IReadOnlyList<IntegrationOperation> All { get; } =
    [
        new(Key, "Send a message", "Sends a text message to a phone number.", [new("to", "To", Hint: "A number, or the employee's mobile field. To test, type your own number here"), new("text", "Message")]),
    ];

    public static readonly IntegrationField DefaultCountry = new("defaultCountry", "Country code for local numbers", Required: false, Hint: "Without the plus, e.g. 91. Numbers stored without a country code get this one");
}

/// <summary>Phone numbers as people type them, made into what messaging services accept.</summary>
public static partial class PhoneNumbers
{
    /// <summary>
    /// The number in international form, digits only and without the plus, or null when it cannot be
    /// one: a local ten-digit number takes the default country, a leading 00 or trunk 0 is dropped.
    /// </summary>
    public static string? Normalize(string? raw, string? defaultCountry)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        string text = raw.Trim();
        string digits = NotDigit().Replace(text, string.Empty);
        string country = NotDigit().Replace(string.IsNullOrWhiteSpace(defaultCountry) ? "91" : defaultCountry, string.Empty);

        if (!text.StartsWith('+'))
        {
            if (digits.StartsWith("00", StringComparison.Ordinal))
            {
                digits = digits[2..];
            }
            else if (digits.Length == 11 && digits[0] == '0')
            {
                digits = country + digits[1..];
            }
            else if (digits.Length == 10)
            {
                digits = country + digits;
            }
        }

        // E.164: at most fifteen digits, and no country code begins with zero.
        return digits.Length is >= 8 and <= 15 && digits[0] != '0' ? digits : null;
    }

    [GeneratedRegex(@"\D")]
    private static partial Regex NotDigit();
}

/// <summary>Twilio's Messages API, for SMS or, with the <c>whatsapp:</c> prefix, for WhatsApp.</summary>
internal abstract class TwilioAdapter(IHttpClientFactory http) : IIntegrationAdapter
{
    public abstract string Key { get; }

    public abstract string Name { get; }

    public string Category => "Messaging";

    public abstract string Description { get; }

    public bool Automatable => true;

    public string? Channel => IntegrationChannels.Sms;

    protected abstract string Prefix { get; }

    public virtual IReadOnlyList<IntegrationField> Fields { get; } =
    [
        new("accountSid", "Account SID"),
        new("authToken", "Auth token", Secret: true),
        new("from", "Send from", Hint: "Your Twilio number, with its country code: +14155550100"),
        SmsOperation.DefaultCountry,
    ];

    public IReadOnlyList<IntegrationOperation> Operations => SmsOperation.All;

    /// <summary>Reads the account, which checks the credentials without sending anything.</summary>
    public async Task<IntegrationResult> TestAsync(IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage request = Authorized(HttpMethod.Get, settings, ".json");
        using HttpResponseMessage response = await http.CreateClient(StudioHttp.ClientName).SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode ? new IntegrationResult(true, "Twilio accepted the credentials.") : new IntegrationResult(false, await StudioHttp.RefusalAsync(response, "Twilio", cancellationToken));
    }

    public async Task<IntegrationResult> RunAsync(string operation, IReadOnlyDictionary<string, string> settings, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken = default)
    {
        if (PhoneNumbers.Normalize(parameters.GetValueOrDefault("to"), settings.GetValueOrDefault("defaultCountry")) is not { } to)
        {
            return new IntegrationResult(false, $"\"{parameters.GetValueOrDefault("to")}\" is not a phone number that can be sent to.");
        }

        using HttpRequestMessage request = Authorized(HttpMethod.Post, settings, "/Messages.json");
        request.Content = new FormUrlEncodedContent(Form(settings, to, parameters.GetValueOrDefault("text") ?? string.Empty));
        using HttpResponseMessage response = await http.CreateClient(StudioHttp.ClientName).SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode ? new IntegrationResult(true, "Sent.", Records: 1) : new IntegrationResult(false, await StudioHttp.RefusalAsync(response, "Twilio", cancellationToken));
    }

    protected virtual Dictionary<string, string> Form(IReadOnlyDictionary<string, string> settings, string to, string text) => new()
    {
        ["To"] = $"{Prefix}+{to}",
        ["From"] = $"{Prefix}{settings.GetValueOrDefault("from")?.Trim()}",
        ["Body"] = text,
    };

    private static HttpRequestMessage Authorized(HttpMethod method, IReadOnlyDictionary<string, string> settings, string path)
    {
        string sid = settings.GetValueOrDefault("accountSid")?.Trim() ?? string.Empty;
        var request = new HttpRequestMessage(method, $"https://api.twilio.com/2010-04-01/Accounts/{Uri.EscapeDataString(sid)}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{sid}:{settings.GetValueOrDefault("authToken")}")));
        return request;
    }
}

internal sealed class TwilioSmsAdapter(IHttpClientFactory http) : TwilioAdapter(http)
{
    public override string Key => "twilio-sms";

    public override string Name => "Twilio SMS";

    public override string Description => "Sends text messages through your Twilio account.";

    protected override string Prefix => string.Empty;
}

/// <summary>WhatsApp through Twilio. Outside a 24-hour customer window WhatsApp accepts only approved templates, sent by content SID with the message as variable 1.</summary>
internal sealed class TwilioWhatsAppAdapter(IHttpClientFactory http) : TwilioAdapter(http)
{
    public override string Key => "twilio-whatsapp";

    public override string Name => "Twilio WhatsApp";

    public override string Description => "Sends WhatsApp messages through your Twilio account.";

    protected override string Prefix => "whatsapp:";

    public override IReadOnlyList<IntegrationField> Fields { get; } =
    [
        new("accountSid", "Account SID"),
        new("authToken", "Auth token", Secret: true),
        new("from", "Send from", Hint: "Your WhatsApp-enabled Twilio number: +14155550100"),
        new("contentSid", "Approved template (content SID)", Required: false, Hint: "HX... WhatsApp needs one to start a conversation. Its {{1}} takes the message"),
        SmsOperation.DefaultCountry,
    ];

    protected override Dictionary<string, string> Form(IReadOnlyDictionary<string, string> settings, string to, string text)
    {
        Dictionary<string, string> form = base.Form(settings, to, text);
        if (settings.GetValueOrDefault("contentSid") is { Length: > 0 } template)
        {
            form.Remove("Body");
            form["ContentSid"] = template.Trim();
            form["ContentVariables"] = JsonSerializer.Serialize(new Dictionary<string, string> { ["1"] = text });
        }

        return form;
    }
}

/// <summary>
/// MSG91 for SMS in India. DLT rules allow only registered templates, so each message goes through
/// one flow, whose ##VAR1## takes the text.
/// </summary>
internal sealed class Msg91Adapter(IHttpClientFactory http) : IIntegrationAdapter
{
    public string Key => "msg91-sms";

    public string Name => "MSG91 SMS";

    public string Category => "Messaging";

    public string Description => "Sends text messages in India through MSG91, using a DLT-approved flow.";

    public bool Automatable => true;

    public string? Channel => IntegrationChannels.Sms;

    public IReadOnlyList<IntegrationField> Fields { get; } =
    [
        new("authKey", "Auth key", Secret: true),
        new("flowId", "Flow ID", Hint: "A flow whose approved template has ##VAR1## where the message goes"),
        SmsOperation.DefaultCountry,
    ];

    public IReadOnlyList<IntegrationOperation> Operations => SmsOperation.All;

    public static JsonObject Message(string flowId, string to, string text) => new()
    {
        ["flow_id"] = flowId,
        ["recipients"] = new JsonArray { new JsonObject { ["mobiles"] = to, ["VAR1"] = text } },
    };

    /// <summary>MSG91 offers nothing that checks a key and flow without sending; the message says so rather than claiming they work.</summary>
    public Task<IntegrationResult> TestAsync(IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken = default)
        => Task.FromResult(new IntegrationResult(true, "Saved, but MSG91 cannot be checked without sending. Send a test from Sending to confirm."));

    public async Task<IntegrationResult> RunAsync(string operation, IReadOnlyDictionary<string, string> settings, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken = default)
    {
        if (PhoneNumbers.Normalize(parameters.GetValueOrDefault("to"), settings.GetValueOrDefault("defaultCountry")) is not { } to)
        {
            return new IntegrationResult(false, $"\"{parameters.GetValueOrDefault("to")}\" is not a phone number that can be sent to.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://control.msg91.com/api/v5/flow/")
        {
            Content = JsonContent.Create(Message(settings.GetValueOrDefault("flowId")?.Trim() ?? string.Empty, to, parameters.GetValueOrDefault("text") ?? string.Empty)),
        };
        request.Headers.Add("authkey", settings.GetValueOrDefault("authKey"));
        using HttpResponseMessage response = await http.CreateClient(StudioHttp.ClientName).SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode ? new IntegrationResult(true, "Sent.", Records: 1) : new IntegrationResult(false, await StudioHttp.RefusalAsync(response, "MSG91", cancellationToken));
    }
}

/// <summary>
/// Meta's WhatsApp Cloud API. A business can start a conversation only with an approved template, so
/// every message uses one, with the text as its first body parameter.
/// </summary>
internal sealed partial class WhatsAppCloudAdapter(IHttpClientFactory http) : IIntegrationAdapter
{
    private const string DefaultVersion = "v22.0";

    public string Key => "whatsapp-cloud";

    public string Name => "WhatsApp Cloud API";

    public string Category => "Messaging";

    public string Description => "Sends WhatsApp messages directly through Meta, using an approved message template.";

    public bool Automatable => true;

    public string? Channel => IntegrationChannels.Sms;

    public IReadOnlyList<IntegrationField> Fields { get; } =
    [
        new("accessToken", "Access token", Secret: true),
        new("phoneNumberId", "Phone number ID"),
        new("templateName", "Approved template name", Hint: "Its {{1}} takes the message"),
        new("languageCode", "Template language", Required: false, Hint: "e.g. en_US; en if left empty"),
        new("apiVersion", "Graph API version", Required: false, Hint: $"Leave empty for {DefaultVersion}; change it when Meta retires that version"),
        SmsOperation.DefaultCountry,
    ];

    public IReadOnlyList<IntegrationOperation> Operations => SmsOperation.All;

    /// <summary>Meta refuses a parameter with line breaks, tabs or long runs of spaces, so the text is flattened first.</summary>
    public static JsonObject Message(string to, string templateName, string? languageCode, string text) => new()
    {
        ["messaging_product"] = "whatsapp",
        ["to"] = to,
        ["type"] = "template",
        ["template"] = new JsonObject
        {
            ["name"] = templateName,
            ["language"] = new JsonObject { ["code"] = string.IsNullOrWhiteSpace(languageCode) ? "en" : languageCode.Trim() },
            ["components"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "body",
                    ["parameters"] = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = Whitespace().Replace(text, " ").Trim() } },
                },
            },
        },
    };

    /// <summary>Reads the phone number, which checks the token and the ID without sending anything.</summary>
    public async Task<IntegrationResult> TestAsync(IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage request = Authorized(HttpMethod.Get, settings, string.Empty);
        using HttpResponseMessage response = await http.CreateClient(StudioHttp.ClientName).SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode ? new IntegrationResult(true, "Meta accepted the token and phone number.") : new IntegrationResult(false, await StudioHttp.RefusalAsync(response, "Meta", cancellationToken));
    }

    public async Task<IntegrationResult> RunAsync(string operation, IReadOnlyDictionary<string, string> settings, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken = default)
    {
        if (PhoneNumbers.Normalize(parameters.GetValueOrDefault("to"), settings.GetValueOrDefault("defaultCountry")) is not { } to)
        {
            return new IntegrationResult(false, $"\"{parameters.GetValueOrDefault("to")}\" is not a phone number that can be sent to.");
        }

        using HttpRequestMessage request = Authorized(HttpMethod.Post, settings, "/messages");
        request.Content = JsonContent.Create(Message(to, settings.GetValueOrDefault("templateName")?.Trim() ?? string.Empty, settings.GetValueOrDefault("languageCode"), parameters.GetValueOrDefault("text") ?? string.Empty));
        using HttpResponseMessage response = await http.CreateClient(StudioHttp.ClientName).SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode ? new IntegrationResult(true, "Sent.", Records: 1) : new IntegrationResult(false, await StudioHttp.RefusalAsync(response, "Meta", cancellationToken));
    }

    private static HttpRequestMessage Authorized(HttpMethod method, IReadOnlyDictionary<string, string> settings, string path)
    {
        string version = settings.GetValueOrDefault("apiVersion")?.Trim() ?? string.Empty;
        version = Version().IsMatch(version) ? version : DefaultVersion;
        var request = new HttpRequestMessage(method, $"https://graph.facebook.com/{version}/{Uri.EscapeDataString(settings.GetValueOrDefault("phoneNumberId")?.Trim() ?? string.Empty)}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.GetValueOrDefault("accessToken"));
        return request;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"^v\d+\.\d+$")]
    private static partial Regex Version();
}
