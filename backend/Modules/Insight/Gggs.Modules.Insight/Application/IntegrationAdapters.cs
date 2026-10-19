using System.Net.Http.Json;
using System.Text.Json;
using Gggs.Modules.Insight.Contracts;

namespace Gggs.Modules.Insight.Application;

internal static class IntegrationHttp
{
    public const string ClientName = "gggs-integrations";
}

/// <summary>
/// Outgoing webhooks: posts a signed JSON event to a URL the tenant gives - the simplest bridge to
/// tools such as Zapier, Teams workflows or an in-house system.
/// </summary>
internal sealed class WebhookAdapter(IHttpClientFactory http, TimeProvider timeProvider) : IIntegrationAdapter
{
    public string Key => "webhook";

    public string Name => "Webhook";

    public string Category => "General";

    public string Description => "Posts a JSON event to a URL, signed with a shared secret in the X-PeopleOS-Signature header.";

    public IReadOnlyList<IntegrationField> Fields { get; } =
    [
        new("url", "Endpoint URL", Hint: "https://..."),
        new("secret", "Signing secret", Secret: true, Required: false),
    ];

    public IReadOnlyList<IntegrationOperation> Operations { get; } =
    [
        new("send", "Send an event", "Posts an event with a type and a message.", [new("event", "Event type", Hint: "e.g. people.announcement"), new("message", "Message")]),
    ];

    public Task<IntegrationResult> TestAsync(IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken = default)
        => PostAsync(settings, "peopleos.ping", "Connection test from PeopleOS.", cancellationToken);

    public Task<IntegrationResult> RunAsync(string operation, IReadOnlyDictionary<string, string> settings, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken = default)
        => PostAsync(settings, parameters.GetValueOrDefault("event") ?? "peopleos.event", parameters.GetValueOrDefault("message") ?? string.Empty, cancellationToken);

    private async Task<IntegrationResult> PostAsync(IReadOnlyDictionary<string, string> settings, string type, string message, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(settings.GetValueOrDefault("url"), UriKind.Absolute, out Uri? url) || url.Scheme != Uri.UriSchemeHttps)
        {
            return new IntegrationResult(false, "The endpoint must be an https URL.");
        }

        string body = JsonSerializer.Serialize(new { type, message, sentAt = timeProvider.GetUtcNow() });
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
        if (settings.GetValueOrDefault("secret") is { Length: > 0 } secret)
        {
            using var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(secret));
            request.Headers.Add("X-PeopleOS-Signature", Convert.ToHexStringLower(hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(body))));
        }

        using HttpResponseMessage response = await http.CreateClient(IntegrationHttp.ClientName).SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode
            ? new IntegrationResult(true, $"Delivered ({(int)response.StatusCode}).", Records: 1)
            : new IntegrationResult(false, $"The endpoint answered {(int)response.StatusCode} {response.ReasonPhrase}.");
    }
}

/// <summary>
/// Microsoft Entra ID (Azure AD): checks an app registration's client credentials and reads the
/// directory's users through Microsoft Graph, so identities can be matched to employees.
/// </summary>
internal sealed class EntraIdAdapter(IHttpClientFactory http) : IIntegrationAdapter
{
    private static readonly string[] UserFields = ["displayName", "mail", "jobTitle"];

    public string Key => "entra-id";

    public string Name => "Microsoft Entra ID";

    public string Category => "Identity";

    public string Description => "Signs in as an app registration (client credentials, User.Read.All) and reads the directory through Microsoft Graph.";

    public IReadOnlyList<IntegrationField> Fields { get; } =
    [
        new("tenantId", "Directory (tenant) ID"),
        new("clientId", "Application (client) ID"),
        new("clientSecret", "Client secret", Secret: true),
    ];

    public IReadOnlyList<IntegrationOperation> Operations { get; } =
    [
        new("list-users", "Preview directory users", "Reads up to 50 users (name, mail, job title) to check matching against employees.", []),
    ];

    public async Task<IntegrationResult> TestAsync(IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken = default)
    {
        string? token = await TokenAsync(settings, cancellationToken);
        return token is null ? new IntegrationResult(false, "Entra ID refused the client credentials.") : new IntegrationResult(true, "Signed in to Microsoft Graph.");
    }

    public async Task<IntegrationResult> RunAsync(string operation, IReadOnlyDictionary<string, string> settings, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken = default)
    {
        string? token = await TokenAsync(settings, cancellationToken);
        if (token is null)
        {
            return new IntegrationResult(false, "Entra ID refused the client credentials.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://graph.microsoft.com/v1.0/users?$top=50&$select=displayName,mail,jobTitle");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        using HttpResponseMessage response = await http.CreateClient(IntegrationHttp.ClientName).SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new IntegrationResult(false, $"Graph answered {(int)response.StatusCode}; the app needs User.Read.All.");
        }

        JsonElement users = (await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).GetProperty("value");
        string csv = "displayName,mail,jobTitle\n" + string.Join('\n', users.EnumerateArray().Select(user => string.Join(',',
            UserFields.Select(field => user.TryGetProperty(field, out JsonElement value) && value.ValueKind == JsonValueKind.String ? $"\"{value.GetString()!.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : string.Empty))));
        return new IntegrationResult(true, $"{users.GetArrayLength()} user(s) read.", "entra-users.csv", "text/csv", System.Text.Encoding.UTF8.GetBytes(csv), users.GetArrayLength());
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
        using HttpResponseMessage response = await http.CreateClient(IntegrationHttp.ClientName).PostAsync(new Uri($"https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token"), content, cancellationToken);
        return response.IsSuccessStatusCode && (await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).TryGetProperty("access_token", out JsonElement token) ? token.GetString() : null;
    }
}
