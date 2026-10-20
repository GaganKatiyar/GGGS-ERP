using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Ai.Domain;

/// <summary>One conversation with an assistant, owned by the person who started it.</summary>
public sealed class AiConversation : TenantAggregateRoot
{
    private AiConversation()
    {
        AssistantKey = null!;
        Title = null!;
    }

    public Guid UserId { get; private set; }

    public Guid? EmployeeId { get; private set; }

    public string AssistantKey { get; private set; }

    public string Title { get; private set; }

    public int Turns { get; private set; }

    public long InputTokens { get; private set; }

    public long OutputTokens { get; private set; }

    public DateTime LastActivityAt { get; private set; }

    public static AiConversation Start(Guid userId, Guid? employeeId, string assistantKey, string firstMessage, DateTime now) => new()
    {
        UserId = Guard.NotEmpty(userId),
        EmployeeId = employeeId,
        AssistantKey = Guard.MaxLength(assistantKey, 40),
        Title = Guard.MaxLength(Shorten(firstMessage, 120), 120),
        LastActivityAt = now,
    };

    public void Used(long inputTokens, long outputTokens, DateTime now)
    {
        Turns++;
        InputTokens += inputTokens;
        OutputTokens += outputTokens;
        LastActivityAt = now;
    }

    private static string Shorten(string text, int length)
    {
        string clean = string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return clean.Length <= length ? (clean.Length == 0 ? "New conversation" : clean) : clean[..(length - 1)] + "…";
    }
}

/// <summary>
/// One message in a conversation, stored as the blocks the model saw (text, tool calls and results,
/// and its thinking with signatures) so the next turn replays the history exactly.
/// </summary>
public sealed class AiMessage : TenantAggregateRoot
{
    private AiMessage()
    {
        Role = null!;
        BlocksJson = "[]";
    }

    public Guid ConversationId { get; private set; }

    public int Sequence { get; private set; }

    /// <summary>user or assistant.</summary>
    public string Role { get; private set; }

    public string BlocksJson { get; private set; }

    public static AiMessage Record(Guid conversationId, int sequence, string role, string blocksJson)
    {
        if (role is not ("user" or "assistant"))
        {
            throw new DomainRuleException("AI_ROLE", "A message is from the user or the assistant.");
        }

        return new AiMessage { ConversationId = conversationId, Sequence = sequence, Role = role, BlocksJson = Guard.NotNullOrWhiteSpace(blocksJson) };
    }
}

/// <summary>A log line for each tool the model used: which, whether it worked and how long it took - not what it returned.</summary>
public sealed class AiToolCall : TenantAggregateRoot
{
    private AiToolCall()
    {
        ToolName = null!;
    }

    public Guid ConversationId { get; private set; }

    public string ToolName { get; private set; }

    public bool Succeeded { get; private set; }

    public int DurationMs { get; private set; }

    public static AiToolCall Log(Guid conversationId, string toolName, bool succeeded, int durationMs) => new()
    {
        ConversationId = conversationId,
        ToolName = Guard.MaxLength(toolName, 64),
        Succeeded = succeeded,
        DurationMs = durationMs,
    };
}

public enum AiActionStatus
{
    Proposed = 1,
    Executed = 2,
    Rejected = 3,
    Failed = 4,
    Expired = 5,
}

/// <summary>
/// Something an assistant proposed doing (spec §89 AIAction / AIActionApproval): nothing happens until
/// the person approves it, and then it runs as them, through the module's own rules.
/// </summary>
public sealed class AiAction : TenantAggregateRoot
{
    /// <summary>Proposals not decided within this many hours lapse rather than run on stale context.</summary>
    public const int ExpiryHours = 24;

    private AiAction()
    {
        ToolName = null!;
        Summary = null!;
        PayloadJson = "{}";
    }

    public Guid ConversationId { get; private set; }

    public Guid UserId { get; private set; }

    public string ToolName { get; private set; }

    public string Summary { get; private set; }

    public string PayloadJson { get; private set; }

    public AiActionStatus Status { get; private set; }

    public DateTime ProposedAt { get; private set; }

    public DateTime? DecidedAt { get; private set; }

    public string? Result { get; private set; }

    public static AiAction Propose(Guid conversationId, Guid userId, string toolName, string summary, string payloadJson, DateTime now) => new()
    {
        ConversationId = conversationId,
        UserId = userId,
        ToolName = Guard.MaxLength(toolName, 64),
        Summary = Guard.MaxLength(Guard.NotNullOrWhiteSpace(summary), 1000),
        PayloadJson = Guard.NotNullOrWhiteSpace(payloadJson),
        Status = AiActionStatus.Proposed,
        ProposedAt = now,
    };

    public bool IsExpired(DateTime now) => Status == AiActionStatus.Proposed && now > ProposedAt.AddHours(ExpiryHours);

    public void EnsureDecidable(DateTime now)
    {
        if (IsExpired(now))
        {
            Status = AiActionStatus.Expired;
            throw new DomainRuleException("AI_ACTION_EXPIRED", "This proposal has lapsed; ask the assistant again.");
        }

        if (Status != AiActionStatus.Proposed)
        {
            throw new DomainRuleException("AI_ACTION_DECIDED", "This proposal has already been decided.");
        }
    }

    public void Executed(string result, DateTime now)
    {
        Status = AiActionStatus.Executed;
        Result = Guard.MaxLength(result, 1000);
        DecidedAt = now;
    }

    public void Failed(string result, DateTime now)
    {
        Status = AiActionStatus.Failed;
        Result = Guard.MaxLength(result, 1000);
        DecidedAt = now;
    }

    public void Reject(DateTime now)
    {
        Status = AiActionStatus.Rejected;
        DecidedAt = now;
    }
}
