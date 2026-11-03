using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Gggs.BuildingBlocks.Web;
using Gggs.Modules.Insight.Contracts;

namespace Gggs.Modules.Studio.Application;

internal static class StudioHttp
{
    public const string ClientName = "studio-connectors";

    /// <summary>Posts JSON to a URL the tenant saved, once the URL has passed <see cref="OutboundUrlGuard"/>.</summary>
    public static async Task<IntegrationResult> PostAsync(IHttpClientFactory http, string? url, JsonNode body, string system, CancellationToken cancellationToken)
    {
        if (await OutboundUrlGuard.CheckAsync(url, cancellationToken) is { } refused)
        {
            return new IntegrationResult(false, refused);
        }

        using HttpResponseMessage response = await http.CreateClient(ClientName).PostAsJsonAsync(new Uri(url!), body, cancellationToken);
        return response.IsSuccessStatusCode
            ? new IntegrationResult(true, $"Posted to {system}.", Records: 1)
            : new IntegrationResult(false, $"{system} answered {(int)response.StatusCode} {response.ReasonPhrase}.");
    }

    /// <summary>What an outside service said when it refused: its status and, when it sent one, the first part of its explanation.</summary>
    public static async Task<string> RefusalAsync(HttpResponseMessage response, string system, CancellationToken cancellationToken)
    {
        string detail = await response.Content.ReadAsStringAsync(cancellationToken);
        detail = detail.Length > 200 ? detail[..200] : detail;
        return $"{system} answered {(int)response.StatusCode} {response.ReasonPhrase}{(detail.Length > 0 ? $": {detail}" : string.Empty)}";
    }

    /// <summary>A link worth showing outside Nuts and Bolts: absolute http(s) only; the app's own relative links mean nothing in a chat.</summary>
    public static string? ExternalLink(string? link)
        => Uri.TryCreate(link, UriKind.Absolute, out Uri? uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) ? uri.ToString() : null;
}

/// <summary>Slack: posts messages to a channel through an incoming webhook the workspace created for Nuts and Bolts.</summary>
internal sealed class SlackAdapter(IHttpClientFactory http) : IIntegrationAdapter
{
    public string Key => "slack";

    public string Name => "Slack";

    public string Category => "Messaging";

    public string Description => "Posts messages to a Slack channel through an incoming webhook - new joiners, approvals, payroll done.";

    public bool Automatable => true;

    public IReadOnlyList<IntegrationField> Fields { get; } =
    [
        new("webhookUrl", "Incoming webhook URL", Secret: true, Hint: "https://hooks.slack.com/services/..."),
    ];

    public IReadOnlyList<IntegrationOperation> Operations { get; } =
    [
        new("post", "Post a message", "Posts a message to the webhook's channel.",
            [new("title", "Title", Required: false), new("text", "Message"), new("link", "Link", Required: false, Hint: "An https address to add as a button")]),
    ];

    /// <summary>Slack's message: a bold title over the text (in Slack's mrkdwn), and a button for the link.</summary>
    public static JsonObject Message(string? title, string text, string? link)
    {
        string body = string.IsNullOrWhiteSpace(title) ? text : $"*{title.Trim()}*\n{text}";
        var blocks = new JsonArray { new JsonObject { ["type"] = "section", ["text"] = new JsonObject { ["type"] = "mrkdwn", ["text"] = body } } };
        if (StudioHttp.ExternalLink(link) is { } url)
        {
            blocks.Add(new JsonObject
            {
                ["type"] = "actions",
                ["elements"] = new JsonArray { new JsonObject { ["type"] = "button", ["text"] = new JsonObject { ["type"] = "plain_text", ["text"] = "Open" }, ["url"] = url } },
            });
        }

        return new JsonObject { ["text"] = body, ["blocks"] = blocks };
    }

    public Task<IntegrationResult> TestAsync(IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken = default)
        => StudioHttp.PostAsync(http, settings.GetValueOrDefault("webhookUrl"), Message("Nuts and Bolts is connected", "Automations can now post to this channel.", null), Name, cancellationToken);

    public Task<IntegrationResult> RunAsync(string operation, IReadOnlyDictionary<string, string> settings, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken = default)
        => StudioHttp.PostAsync(http, settings.GetValueOrDefault("webhookUrl"), Message(parameters.GetValueOrDefault("title"), parameters.GetValueOrDefault("text") ?? string.Empty, parameters.GetValueOrDefault("link")), Name, cancellationToken);
}

/// <summary>Microsoft Teams: posts an Adaptive Card to a channel or chat through a Teams Workflows ("When a Teams webhook request is received") URL.</summary>
internal sealed class TeamsAdapter(IHttpClientFactory http) : IIntegrationAdapter
{
    public string Key => "teams";

    public string Name => "Microsoft Teams";

    public string Category => "Messaging";

    public string Description => "Posts cards to a Teams channel or chat through a Workflows webhook - new joiners, approvals, payroll done.";

    public bool Automatable => true;

    public IReadOnlyList<IntegrationField> Fields { get; } =
    [
        new("webhookUrl", "Workflows webhook URL", Secret: true, Hint: "From the Teams workflow \"Post to a channel when a webhook request is received\""),
    ];

    public IReadOnlyList<IntegrationOperation> Operations { get; } =
    [
        new("post", "Post a card", "Posts an Adaptive Card to the workflow's channel or chat.",
            [new("title", "Title", Required: false), new("text", "Message"), new("link", "Link", Required: false, Hint: "An https address to add as a button")]),
    ];

    /// <summary>The message a Teams workflow expects: one Adaptive Card attachment with the title, the text and a button for the link.</summary>
    public static JsonObject Message(string? title, string text, string? link)
    {
        var body = new JsonArray();
        if (!string.IsNullOrWhiteSpace(title))
        {
            body.Add(new JsonObject { ["type"] = "TextBlock", ["text"] = title.Trim(), ["weight"] = "Bolder", ["size"] = "Medium", ["wrap"] = true });
        }

        body.Add(new JsonObject { ["type"] = "TextBlock", ["text"] = text, ["wrap"] = true });
        var card = new JsonObject
        {
            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
            ["type"] = "AdaptiveCard",
            ["version"] = "1.4",
            ["body"] = body,
        };
        if (StudioHttp.ExternalLink(link) is { } url)
        {
            card["actions"] = new JsonArray { new JsonObject { ["type"] = "Action.OpenUrl", ["title"] = "Open", ["url"] = url } };
        }

        return new JsonObject
        {
            ["type"] = "message",
            ["attachments"] = new JsonArray { new JsonObject { ["contentType"] = "application/vnd.microsoft.card.adaptive", ["content"] = card } },
        };
    }

    public Task<IntegrationResult> TestAsync(IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken = default)
        => StudioHttp.PostAsync(http, settings.GetValueOrDefault("webhookUrl"), Message("Nuts and Bolts is connected", "Automations can now post here.", null), Name, cancellationToken);

    public Task<IntegrationResult> RunAsync(string operation, IReadOnlyDictionary<string, string> settings, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken = default)
        => StudioHttp.PostAsync(http, settings.GetValueOrDefault("webhookUrl"), Message(parameters.GetValueOrDefault("title"), parameters.GetValueOrDefault("text") ?? string.Empty, parameters.GetValueOrDefault("link")), Name, cancellationToken);
}
