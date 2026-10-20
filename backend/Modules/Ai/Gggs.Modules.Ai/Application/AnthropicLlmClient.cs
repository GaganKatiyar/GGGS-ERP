using System.Text.Json;
using Anthropic;
using Anthropic.Models.Beta;
using Anthropic.Models.Beta.Messages;
using Microsoft.Extensions.Options;

namespace Gggs.Modules.Ai.Application;

/// <summary>
/// Claude through the official Anthropic SDK. Thinking stays on (adaptive, the model's default) at
/// medium effort, and a request the model declines is re-served by the fallback model on the
/// server (server-side fallback) rather than ending the assistant's turn.
/// </summary>
internal sealed class AnthropicLlmClient(IOptions<AiOptions> options) : ILlmClient
{
    private readonly Lazy<AnthropicClient> client = new(() => new AnthropicClient { ApiKey = options.Value.ResolvedApiKey });

    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Value.ResolvedApiKey);

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        AiOptions settings = options.Value;
        var parameters = new MessageCreateParams
        {
            Model = settings.Model,
            MaxTokens = settings.MaxTokens,
            Betas = [AnthropicBeta.ServerSideFallback2026_06_01],
            Fallbacks = new List<BetaFallbackParam> { new() { Model = settings.FallbackModel } },
            OutputConfig = new BetaOutputConfig { Effort = Effort.Medium },
            System = request.System,
            Tools = [.. request.Tools.Select(ToTool)],
            Messages = [.. request.Messages.Select(ToMessage)],
        };

        BetaMessage response = await client.Value.Beta.Messages.Create(parameters, cancellationToken);
        List<LlmBlock> blocks = [];
        foreach (BetaContentBlock block in response.Content)
        {
            if (block.TryPickText(out BetaTextBlock? text))
            {
                blocks.Add(new LlmText(text.Text));
            }
            else if (block.TryPickThinking(out BetaThinkingBlock? thinking))
            {
                blocks.Add(new LlmThinking(thinking.Thinking, thinking.Signature));
            }
            else if (block.TryPickRedactedThinking(out BetaRedactedThinkingBlock? redacted))
            {
                blocks.Add(new LlmRedactedThinking(redacted.Data));
            }
            else if (block.TryPickToolUse(out BetaToolUseBlock? toolUse))
            {
                blocks.Add(new LlmToolUse(toolUse.ID, toolUse.Name, JsonSerializer.SerializeToElement(toolUse.Input)));
            }
        }

        return new LlmResponse(blocks, response.StopReason?.Raw() ?? "end_turn", response.Usage.InputTokens, response.Usage.OutputTokens, response.Model.Raw() ?? settings.Model);
    }

    private static BetaToolUnion ToTool(LlmTool tool)
    {
        Dictionary<string, JsonElement> properties = [];
        List<string> required = [];
        if (tool.InputSchema.TryGetProperty("properties", out JsonElement props))
        {
            foreach (JsonProperty property in props.EnumerateObject())
            {
                properties[property.Name] = property.Value.Clone();
            }
        }

        if (tool.InputSchema.TryGetProperty("required", out JsonElement names))
        {
            required.AddRange(names.EnumerateArray().Select(name => name.GetString()!));
        }

        return new BetaTool
        {
            Name = tool.Name,
            Description = tool.Description,
            InputSchema = new() { Properties = properties, Required = required },
        };
    }

    private static BetaMessageParam ToMessage(LlmMessage message)
    {
        List<BetaContentBlockParam> content = [];
        foreach (LlmBlock block in message.Blocks)
        {
            switch (block)
            {
                case LlmText text:
                    content.Add(new BetaTextBlockParam { Text = text.Text });
                    break;
                case LlmThinking thinking:
                    // Replayed unchanged: the signature binds it to this model and conversation.
                    content.Add(new BetaThinkingBlockParam { Thinking = thinking.Thinking, Signature = thinking.Signature });
                    break;
                case LlmRedactedThinking redacted:
                    content.Add(new BetaRedactedThinkingBlockParam { Data = redacted.Data });
                    break;
                case LlmToolUse toolUse:
                    content.Add(new BetaToolUseBlockParam
                    {
                        ID = toolUse.Id,
                        Name = toolUse.Name,
                        Input = toolUse.Input.ValueKind == JsonValueKind.Object
                            ? toolUse.Input.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone())
                            : [],
                    });
                    break;
                case LlmToolResult result:
                    content.Add(new BetaToolResultBlockParam { ToolUseID = result.ToolUseId, Content = result.Content, IsError = result.IsError });
                    break;
            }
        }

        return new BetaMessageParam { Role = message.Role == "assistant" ? Role.Assistant : Role.User, Content = content };
    }
}
