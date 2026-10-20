using System.Diagnostics;
using System.Text.Json;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Ai.Contracts;
using Gggs.Modules.Ai.Domain;
using Gggs.Modules.Ai.Infrastructure;
using Gggs.Modules.Employee.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Gggs.Modules.Ai.Application;

public sealed record AssistantItem(string Key, string Title, string Description, IReadOnlyList<string> Tools);

public sealed record AiStatus(bool Configured, string Model, IReadOnlyList<AssistantItem> Assistants);

public sealed record ConversationItem(Guid Id, string AssistantKey, string Title, int Turns, DateTime LastActivityAt);

public sealed record ActionItem(Guid Id, string ToolName, string Summary, AiActionStatus Status, DateTime ProposedAt, DateTime? DecidedAt, string? Result);

public sealed record ChatMessage(string Role, string Text, IReadOnlyList<string> ToolsUsed, IReadOnlyList<Guid> ActionIds);

public sealed record ConversationView(ConversationItem Conversation, IReadOnlyList<ChatMessage> Messages, IReadOnlyList<ActionItem> Actions);

public sealed record SendBody(string Message, string? Assistant);

public sealed record UsageRow(string AssistantKey, int Conversations, int Turns, long InputTokens, long OutputTokens, int ToolCalls, int ActionsProposed, int ActionsExecuted);

/// <summary>
/// The assistants (spec §89): a conversation loop over the model and the tools modules register.
/// Every tool runs as the signed-in user; tools the user may not use are never offered; action
/// tools only propose, and a proposal runs only when the user approves it. Conversations, tool
/// calls and token usage are logged; conversations are deleted after the retention period.
/// </summary>
internal sealed class AiService(
    AppDbContext dbContext,
    ILlmClient llm,
    IEnumerable<IAiTool> tools,
    IPermissionProvider permissions,
    ICurrentUser currentUser,
    IEmployeeDirectory employees,
    ITenantContext tenantContext,
    ITenantRegistry tenants,
    ITenantClock clock,
    IOptions<AiOptions> options,
    TimeProvider timeProvider,
    ILogger<AiService> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    public async Task<AiStatus> StatusAsync(CancellationToken cancellationToken)
    {
        PermissionSet granted = await permissions.GetCurrentAsync(cancellationToken);
        return new AiStatus(llm.IsConfigured, options.Value.Model, [.. Assistants.All
            .Where(assistant => assistant.Permission is null || granted.Has(assistant.Permission))
            .Select(assistant => new AssistantItem(assistant.Key, assistant.Title, assistant.Description, [.. ToolsFor(assistant, granted).Select(tool => tool.Name)]))]);
    }

    public async Task<IReadOnlyList<ConversationItem>> ConversationsAsync(CancellationToken cancellationToken)
    {
        Guid me = currentUser.UserId;
        return [.. (await dbContext.AiConversations.AsNoTracking().Where(row => row.UserId == me).OrderByDescending(row => row.LastActivityAt).Take(50).ToListAsync(cancellationToken)).Select(Item)];
    }

    public async Task<ConversationView> ConversationAsync(Guid id, CancellationToken cancellationToken)
        => await ViewAsync(await OwnAsync(id, tracked: false, cancellationToken), cancellationToken);

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        AiConversation conversation = await OwnAsync(id, tracked: true, cancellationToken);
        dbContext.AiConversations.Remove(conversation);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Sends a message - starting a conversation when <paramref name="conversationId"/> is null - and runs the assistant's turn.</summary>
    public async Task<ConversationView> SendAsync(Guid? conversationId, SendBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        string text = (body.Message ?? string.Empty).Trim();
        if (text.Length is 0 or > 4000)
        {
            throw new DomainRuleException("AI_MESSAGE", "Write a message of up to 4,000 characters.");
        }

        if (!llm.IsConfigured)
        {
            throw new DomainRuleException("AI_NOT_CONFIGURED", "The assistant is not set up for this organisation yet.");
        }

        PermissionSet granted = await permissions.GetCurrentAsync(cancellationToken);
        AiConversation conversation;
        if (conversationId is { } existing)
        {
            conversation = await OwnAsync(existing, tracked: true, cancellationToken);
        }
        else
        {
            AssistantDefinition chosen = Assistants.Find(body.Assistant) ?? throw new DomainRuleException("AI_ASSISTANT", "Choose an assistant.");
            if (chosen.Permission is not null && !granted.Has(chosen.Permission))
            {
                throw new ForbiddenException("AI_ASSISTANT", $"The {chosen.Title} needs access you do not have.");
            }

            conversation = AiConversation.Start(currentUser.UserId, currentUser.EmployeeId, chosen.Key, text, Now);
            dbContext.AiConversations.Add(conversation);
        }

        AssistantDefinition assistant = Assistants.Find(conversation.AssistantKey) ?? throw new DomainRuleException("AI_ASSISTANT", "This assistant is no longer available.");
        Dictionary<string, IAiTool> offered = ToolsFor(assistant, granted).ToDictionary(tool => tool.Name);

        List<AiMessage> stored = await dbContext.AiMessages.Where(row => row.ConversationId == conversation.Id).OrderBy(row => row.Sequence).ToListAsync(cancellationToken);
        List<LlmMessage> history = [.. stored.Select(row => new LlmMessage(row.Role, Blocks(row.BlocksJson)))];
        int sequence = stored.Count;

        // Tell the model what became of its proposals since the last turn, so it does not ask again.
        List<LlmBlock> userBlocks = [];
        foreach (AiAction decided in await dbContext.AiActions.AsNoTracking().Where(row => row.ConversationId == conversation.Id && row.DecidedAt != null && row.DecidedAt > conversation.LastActivityAt).ToListAsync(cancellationToken))
        {
            userBlocks.Add(new LlmText($"[Update: the proposal \"{decided.Summary}\" was {decided.Status.ToString().ToLowerInvariant()}{(decided.Result is null ? string.Empty : ": " + decided.Result)}]"));
        }

        userBlocks.Add(new LlmText(text));
        Append("user", userBlocks);

        string tenantName = (await tenants.GetAsync(tenantContext.TenantId, cancellationToken))?.Name ?? "your organisation";
        string userName = currentUser.EmployeeId is { } employeeId ? (await employees.GetAsync(employeeId, cancellationToken))?.DisplayName ?? "a colleague" : "an administrator";
        string system = Assistants.SystemPrompt(assistant, tenantName, userName, await clock.TodayAsync(cancellationToken));
        List<LlmTool> toolDefinitions = [.. offered.Values.OrderBy(tool => tool.Name, StringComparer.Ordinal).Select(tool => new LlmTool(tool.Name, Describe(tool), tool.InputSchema))];

        long input = 0;
        long output = 0;
        for (int round = 0; round < Math.Max(1, options.Value.MaxToolRounds); round++)
        {
            LlmResponse response = await llm.CompleteAsync(new LlmRequest(system, toolDefinitions, [.. history]), cancellationToken);
            input += response.InputTokens;
            output += response.OutputTokens;
            List<LlmBlock> reply = [.. response.Blocks];
            if (response.StopReason == "refusal" && !reply.OfType<LlmText>().Any())
            {
                reply.Add(new LlmText("I can't help with that request."));
            }

            Append("assistant", reply);
            List<LlmToolUse> calls = [.. reply.OfType<LlmToolUse>()];
            if (response.StopReason != "tool_use" || calls.Count == 0)
            {
                break;
            }

            List<LlmBlock> results = [];
            foreach (LlmToolUse call in calls)
            {
                results.Add(await RunToolAsync(conversation, offered, call, cancellationToken));
            }

            Append("user", results);
        }

        conversation.Used(input, output, Now);
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Assistant {Assistant} turn: {Input} input and {Output} output tokens.", assistant.Key, input, output);
        return await ViewAsync(conversation, cancellationToken);

        void Append(string role, List<LlmBlock> blocks)
        {
            history.Add(new LlmMessage(role, blocks));
            dbContext.AiMessages.Add(AiMessage.Record(conversation.Id, sequence++, role, JsonSerializer.Serialize<IReadOnlyList<LlmBlock>>(blocks, Json)));
        }
    }

    /// <summary>Runs a proposal the user approves, as the user, through the tool's module.</summary>
    public async Task<ActionItem> ApproveAsync(Guid id, CancellationToken cancellationToken)
    {
        AiAction action = await OwnActionAsync(id, cancellationToken);
        try
        {
            action.EnsureDecidable(Now);
        }
        catch (DomainRuleException)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            throw;
        }

        PermissionSet granted = await permissions.GetCurrentAsync(cancellationToken);
        IAiActionTool tool = tools.OfType<IAiActionTool>().FirstOrDefault(candidate => candidate.Name == action.ToolName && (candidate.Permission is null || granted.Has(candidate.Permission)))
            ?? throw new ForbiddenException("AI_ACTION_TOOL", "You can no longer do this.");
        try
        {
            action.Executed(await tool.ExecuteAsync(action.PayloadJson, cancellationToken), Now);
        }
        catch (Exception exception) when (exception is DomainRuleException or NotFoundException or ForbiddenException)
        {
            action.Failed(exception.Message, Now);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return Action(action);
    }

    public async Task<ActionItem> RejectAsync(Guid id, CancellationToken cancellationToken)
    {
        AiAction action = await OwnActionAsync(id, cancellationToken);
        action.EnsureDecidable(Now);
        action.Reject(Now);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Action(action);
    }

    public async Task<IReadOnlyList<UsageRow>> UsageAsync(int days, CancellationToken cancellationToken)
    {
        DateTime since = Now.AddDays(-Math.Clamp(days, 1, 365));
        List<AiConversation> conversations = await dbContext.AiConversations.AsNoTracking().Where(row => row.LastActivityAt >= since).ToListAsync(cancellationToken);
        List<Guid> ids = [.. conversations.Select(row => row.Id)];
        var calls = await dbContext.AiToolCalls.AsNoTracking().Where(row => ids.Contains(row.ConversationId)).GroupBy(row => row.ConversationId).Select(group => new { group.Key, Count = group.Count() }).ToDictionaryAsync(row => row.Key, row => row.Count, cancellationToken);
        var actions = await dbContext.AiActions.AsNoTracking().Where(row => ids.Contains(row.ConversationId)).Select(row => new { row.ConversationId, row.Status }).ToListAsync(cancellationToken);
        return [.. conversations.GroupBy(row => row.AssistantKey).Select(group => new UsageRow(
            group.Key,
            group.Count(),
            group.Sum(row => row.Turns),
            group.Sum(row => row.InputTokens),
            group.Sum(row => row.OutputTokens),
            group.Sum(row => calls.GetValueOrDefault(row.Id)),
            actions.Count(action => group.Any(row => row.Id == action.ConversationId)),
            actions.Count(action => action.Status == AiActionStatus.Executed && group.Any(row => row.Id == action.ConversationId))))
            .OrderByDescending(row => row.InputTokens + row.OutputTokens)];
    }

    /// <summary>Deletes conversations idle past the retention period, with their messages and logs.</summary>
    public async Task<int> PurgeAsync(CancellationToken cancellationToken)
    {
        DateTime cutoff = Now.AddDays(-Math.Max(1, options.Value.RetentionDays));
        List<AiConversation> old = await dbContext.AiConversations.Where(row => row.LastActivityAt < cutoff).Take(500).ToListAsync(cancellationToken);
        dbContext.AiConversations.RemoveRange(old);
        await dbContext.SaveChangesAsync(cancellationToken);
        return old.Count;
    }

    // ----- Internals ----------------------------------------------------------------------------

    private static string Describe(IAiTool tool)
        => tool.Kind == AiToolKind.Action ? $"{tool.Description} This is an action: it only drafts a proposal for the person to approve." : tool.Description;

    private static List<LlmBlock> Blocks(string json) => JsonSerializer.Deserialize<List<LlmBlock>>(json, Json) ?? [];

    private static ConversationItem Item(AiConversation row) => new(row.Id, row.AssistantKey, row.Title, row.Turns, row.LastActivityAt);

    private static ActionItem Action(AiAction row) => new(row.Id, row.ToolName, row.Summary, row.Status, row.ProposedAt, row.DecidedAt, row.Result);

    private IEnumerable<IAiTool> ToolsFor(AssistantDefinition assistant, PermissionSet granted)
        => tools.Where(tool => assistant.Tools.Contains(tool.Name) && (tool.Permission is null || granted.Has(tool.Permission)));

    private async Task<LlmToolResult> RunToolAsync(AiConversation conversation, Dictionary<string, IAiTool> offered, LlmToolUse call, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        LlmToolResult result;
        if (!offered.TryGetValue(call.Name, out IAiTool? tool))
        {
            result = new LlmToolResult(call.Id, $"The tool {call.Name} is not available to this person.", IsError: true);
        }
        else
        {
            try
            {
                if (tool is IAiActionTool action)
                {
                    AiProposal proposal = await action.ProposeAsync(call.Input, cancellationToken);
                    var proposed = AiAction.Propose(conversation.Id, currentUser.UserId, tool.Name, proposal.Summary, proposal.PayloadJson, Now);
                    dbContext.AiActions.Add(proposed);
                    result = new LlmToolResult(call.Id, $"Proposal {proposed.Id} drafted: {proposal.Summary}. It has NOT been done - the person must approve it in the assistant panel.", IsError: false);
                }
                else
                {
                    AiToolOutput output = await tool.RunAsync(call.Input, cancellationToken);
                    result = new LlmToolResult(call.Id, output.Content.Length > 20000 ? output.Content[..20000] + " [truncated]" : output.Content, !output.Succeeded);
                }
            }
            catch (Exception exception) when (exception is DomainRuleException or NotFoundException or ForbiddenException or ArgumentException or InvalidOperationException)
            {
                result = new LlmToolResult(call.Id, exception.Message, IsError: true);
            }
        }

        dbContext.AiToolCalls.Add(AiToolCall.Log(conversation.Id, call.Name, !result.IsError, (int)watch.ElapsedMilliseconds));
        return result;
    }

    private async Task<AiConversation> OwnAsync(Guid id, bool tracked, CancellationToken cancellationToken)
    {
        Guid me = currentUser.UserId;
        IQueryable<AiConversation> source = tracked ? dbContext.AiConversations : dbContext.AiConversations.AsNoTracking();
        return await source.FirstOrDefaultAsync(row => row.Id == id && row.UserId == me, cancellationToken) ?? throw new NotFoundException(nameof(AiConversation), id);
    }

    private async Task<AiAction> OwnActionAsync(Guid id, CancellationToken cancellationToken)
    {
        Guid me = currentUser.UserId;
        return await dbContext.AiActions.FirstOrDefaultAsync(row => row.Id == id && row.UserId == me, cancellationToken) ?? throw new NotFoundException(nameof(AiAction), id);
    }

    private async Task<ConversationView> ViewAsync(AiConversation conversation, CancellationToken cancellationToken)
    {
        List<AiMessage> stored = await dbContext.AiMessages.AsNoTracking().Where(row => row.ConversationId == conversation.Id).OrderBy(row => row.Sequence).ToListAsync(cancellationToken);
        List<AiAction> actions = await dbContext.AiActions.AsNoTracking().Where(row => row.ConversationId == conversation.Id).OrderBy(row => row.ProposedAt).ToListAsync(cancellationToken);
        List<ChatMessage> messages = [];
        foreach (AiMessage row in stored)
        {
            IReadOnlyList<LlmBlock> blocks = Blocks(row.BlocksJson);
            string text = string.Join("\n\n", blocks.OfType<LlmText>().Select(block => block.Text).Where(value => !value.StartsWith("[Update:", StringComparison.Ordinal)));
            List<string> toolsUsed = [.. blocks.OfType<LlmToolUse>().Select(block => block.Name)];
            List<Guid> actionIds = [.. blocks.OfType<LlmToolResult>().Select(block => ProposalId(block.Content)).Where(value => value != null).Select(value => value!.Value)];
            if (row.Role == "user" && text.Length == 0)
            {
                // Tool results only: attach any proposals to the assistant message that asked for them.
                if (actionIds.Count > 0 && messages.Count > 0 && messages[^1].Role == "assistant")
                {
                    messages[^1] = messages[^1] with { ActionIds = [.. messages[^1].ActionIds, .. actionIds] };
                }

                continue;
            }

            if (row.Role == "assistant" && messages.Count > 0 && messages[^1].Role == "assistant")
            {
                // Consecutive assistant rounds (tool use, then the answer) read as one reply.
                ChatMessage previous = messages[^1];
                messages[^1] = previous with { Text = string.Join("\n\n", new[] { previous.Text, text }.Where(value => value.Length > 0)), ToolsUsed = [.. previous.ToolsUsed, .. toolsUsed] };
                continue;
            }

            messages.Add(new ChatMessage(row.Role, text, toolsUsed, []));
        }

        DateTime now = Now;
        return new ConversationView(Item(conversation), messages, [.. actions.Select(row => Action(row) with { Status = row.IsExpired(now) ? AiActionStatus.Expired : row.Status })]);
    }

    private static Guid? ProposalId(string content)
        => content.StartsWith("Proposal ", StringComparison.Ordinal) && Guid.TryParse(content.AsSpan(9, Math.Min(36, content.Length - 9)), out Guid id) ? id : null;
}
