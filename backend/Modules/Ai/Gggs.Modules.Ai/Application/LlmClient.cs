using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gggs.Modules.Ai.Application;

/// <summary>One block of a message, in the provider-neutral form the conversation is stored in.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(LlmText), "text")]
[JsonDerivedType(typeof(LlmToolUse), "tool_use")]
[JsonDerivedType(typeof(LlmToolResult), "tool_result")]
[JsonDerivedType(typeof(LlmThinking), "thinking")]
[JsonDerivedType(typeof(LlmRedactedThinking), "redacted_thinking")]
public abstract record LlmBlock;

public sealed record LlmText(string Text) : LlmBlock;

public sealed record LlmToolUse(string Id, string Name, JsonElement Input) : LlmBlock;

public sealed record LlmToolResult(string ToolUseId, string Content, bool IsError) : LlmBlock;

/// <summary>The model's thinking, replayed unchanged with its signature on the next turn.</summary>
public sealed record LlmThinking(string Thinking, string Signature) : LlmBlock;

public sealed record LlmRedactedThinking(string Data) : LlmBlock;

public sealed record LlmMessage(string Role, IReadOnlyList<LlmBlock> Blocks);

public sealed record LlmTool(string Name, string Description, JsonElement InputSchema);

public sealed record LlmRequest(string System, IReadOnlyList<LlmTool> Tools, IReadOnlyList<LlmMessage> Messages);

/// <summary>The model's reply. <see cref="StopReason"/> is the API's: end_turn, tool_use, max_tokens, refusal, pause_turn.</summary>
public sealed record LlmResponse(IReadOnlyList<LlmBlock> Blocks, string StopReason, long InputTokens, long OutputTokens, string Model);

/// <summary>The language model behind the assistants, kept behind a port so the loop is testable without the network.</summary>
public interface ILlmClient
{
    /// <summary>Whether a model is configured; the assistants are switched off otherwise.</summary>
    bool IsConfigured { get; }

    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Settings under <c>Gggs:Ai</c>.</summary>
public sealed class AiOptions
{
    public const string Section = "Gggs:Ai";

    /// <summary>The Anthropic API key; when empty, <c>ANTHROPIC_API_KEY</c> is used, and with neither the assistants are off.</summary>
    public string? ApiKey { get; set; }

    public string Model { get; set; } = "claude-opus-5-5";

    /// <summary>The model a refused request is re-served by (server-side fallback).</summary>
    public string FallbackModel { get; set; } = "claude-opus-4-8";

    public int MaxTokens { get; set; } = 16000;

    /// <summary>Tool rounds the assistant may take in one turn before it must answer.</summary>
    public int MaxToolRounds { get; set; } = 8;

    /// <summary>Days conversations are kept before they are deleted.</summary>
    public int RetentionDays { get; set; } = 30;

    public string ResolvedApiKey => string.IsNullOrWhiteSpace(ApiKey) ? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? string.Empty : ApiKey;
}
