using System.Collections.Concurrent;
using System.Text.Json;
using Gggs.Modules.Ai.Application;

namespace Gggs.Api.IntegrationTests;

/// <summary>
/// A stand-in for the model: tests queue the replies it gives, in order, and read back the requests
/// it received - the tools it was offered and the tool results it was shown.
/// </summary>
public sealed class ScriptedLlmClient : ILlmClient
{
    private readonly ConcurrentQueue<LlmResponse> replies = new();

    public bool IsConfigured => true;

    public ConcurrentQueue<LlmRequest> Requests { get; } = new();

    public void Reset()
    {
        replies.Clear();
        Requests.Clear();
    }

    public void ReplyText(string text) => replies.Enqueue(new LlmResponse([new LlmText(text)], "end_turn", 100, 20, "scripted"));

    public void ReplyToolUse(string name, object input, string? preamble = null)
    {
        List<LlmBlock> blocks = [];
        if (preamble is not null)
        {
            blocks.Add(new LlmText(preamble));
        }

        blocks.Add(new LlmToolUse($"toolu_{Guid.NewGuid():N}", name, JsonSerializer.SerializeToElement(input)));
        replies.Enqueue(new LlmResponse(blocks, "tool_use", 120, 30, "scripted"));
    }

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Enqueue(request);
        return Task.FromResult(replies.TryDequeue(out LlmResponse? reply) ? reply : new LlmResponse([new LlmText("(no scripted reply)")], "end_turn", 1, 1, "scripted"));
    }
}
