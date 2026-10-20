using System.Text.Json;

namespace Gggs.Modules.Ai.Contracts;

public enum AiToolKind
{
    /// <summary>Reads data the caller may already see; runs as soon as the model asks.</summary>
    Read = 1,

    /// <summary>Changes something; the model can only propose it, and a person approves before it runs.</summary>
    Action = 2,
}

/// <summary>What a read tool returns to the model: compact JSON, or a short refusal to show it.</summary>
public sealed record AiToolOutput(bool Succeeded, string Content)
{
    public static AiToolOutput Data(object value) => new(true, JsonSerializer.Serialize(value, AiJson.Options));

    public static AiToolOutput Problem(string message) => new(false, message);
}

/// <summary>What an action tool proposes: a sentence for the person to approve, and the payload to run if they do.</summary>
public sealed record AiProposal(string Summary, string PayloadJson);

/// <summary>
/// A tool the assistants can use (spec §89). It runs as the signed-in user - their permissions and
/// data scope apply exactly as they do in the screens - and reads through its own module's services,
/// never the database directly. Action tools never act: they propose, and a person approves.
/// </summary>
public interface IAiTool
{
    /// <summary>snake_case, unique across modules.</summary>
    string Name { get; }

    /// <summary>What the tool does and when to use it, written for the model.</summary>
    string Description { get; }

    /// <summary>A JSON Schema object for the input.</summary>
    JsonElement InputSchema { get; }

    AiToolKind Kind { get; }

    /// <summary>The permission the user needs for the tool to be offered at all; null for everyone signed in.</summary>
    string? Permission { get; }

    /// <summary>Read tools: run and return data. Action tools: return a proposal (as JSON) without changing anything.</summary>
    Task<AiToolOutput> RunAsync(JsonElement input, CancellationToken cancellationToken = default);
}

/// <summary>An action tool: proposes from the model's input, and executes the approved payload.</summary>
public interface IAiActionTool : IAiTool
{
    Task<AiProposal> ProposeAsync(JsonElement input, CancellationToken cancellationToken = default);

    /// <summary>Runs the approved payload as the approving user; returns what happened, in a sentence.</summary>
    Task<string> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default);
}

/// <summary>Shared helpers for writing tools.</summary>
public static class AiJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);

    /// <summary>Builds an input schema from property definitions: <c>("name", "string", "description", required)</c>.</summary>
    public static JsonElement Schema(params (string Name, string Type, string Description, bool Required)[] properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        var props = new Dictionary<string, object>();
        foreach ((string name, string type, string description, _) in properties)
        {
            props[name] = new { type, description };
        }

        return JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = props,
            required = properties.Where(property => property.Required).Select(property => property.Name).ToArray(),
            additionalProperties = false,
        });
    }

    public static string? Text(JsonElement input, string name)
        => input.ValueKind == JsonValueKind.Object && input.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    public static int? Number(JsonElement input, string name)
        => input.ValueKind == JsonValueKind.Object && input.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number) ? number : null;

    public static bool? Flag(JsonElement input, string name)
        => input.ValueKind == JsonValueKind.Object && input.TryGetProperty(name, out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;

    public static DateOnly? Date(JsonElement input, string name)
        => DateOnly.TryParse(Text(input, name), System.Globalization.CultureInfo.InvariantCulture, out DateOnly date) ? date : null;
}
