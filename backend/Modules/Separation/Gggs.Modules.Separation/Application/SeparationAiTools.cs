using System.Text.Json;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Security;
using Gggs.Modules.Ai.Contracts;

namespace Gggs.Modules.Separation.Application;

/// <summary>The caller's own separation: where it stands and what is left to do.</summary>
internal sealed class MySeparationStatusTool(SeparationService separations) : IAiTool
{
    public string Name => "my_separation_status";

    public string Description => "The signed-in employee's own resignation or separation: status, last working day, notice, clearance and offboarding steps still open, and the full and final settlement. Use when they ask about leaving, their last day, clearance or their settlement.";

    public JsonElement InputSchema { get; } = AiJson.Schema();

    public AiToolKind Kind => AiToolKind.Read;

    public string? Permission => Permissions.Self.ResignationSubmit;

    public async Task<AiToolOutput> RunAsync(JsonElement input, CancellationToken cancellationToken = default)
    {
        CaseView? view = await separations.MineAsync(cancellationToken);
        if (view is null)
        {
            return AiToolOutput.Problem("You have no resignation or separation on record.");
        }

        CaseItem item = view.Case;
        return AiToolOutput.Data(new
        {
            Type = SeparationService.Label(item.Type),
            Status = item.Status.ToString(),
            item.NoticeDate,
            item.NoticePeriodDays,
            item.LastWorkingDay,
            NoticeShortfallDays = item.ShortfallWaived ? 0 : item.ShortfallDays,
            item.RequestedReleaseDay,
            ClearancePending = view.Clearance.Where(row => row.Status == Domain.ClearanceStatus.Pending).Select(row => row.Title),
            MyOffboardingSteps = view.Offboarding?.Tasks.Where(task => task.Owner == Domain.OffboardingOwner.Employee && task.Status == Domain.OffboardingTaskStatus.Pending).Select(task => new { task.Title, task.DueDate }),
            ExitInterviewDone = item.InterviewDone,
            Settlement = view.Settlement is { } settlement ? new { Status = settlement.Status.ToString(), settlement.Net, settlement.PaidOn } : null,
            item.SettlementDueOn,
        });
    }
}

/// <summary>Attrition over a window for HR: rates, regretted losses, reasons, tenure and hot spots.</summary>
internal sealed class AttritionInsightsTool(SeparationAnalytics analytics) : IAiTool
{
    public string Name => "attrition_insights";

    public string Description => "Attrition analytics within the caller's reach for the last N months (default 12): annualised attrition rate, voluntary/involuntary/natural exits, regretted share, early attrition (under six months), top reasons, tenure bands, departments and managers with most exits, the monthly trend, notice served, settlement turnaround and exit survey scores.";

    public JsonElement InputSchema { get; } = AiJson.Schema(("months", "integer", "How many months back to look, 1-36 (default 12)", false));

    public AiToolKind Kind => AiToolKind.Read;

    public string? Permission => Permissions.Separation.AnalyticsView;

    public async Task<AiToolOutput> RunAsync(JsonElement input, CancellationToken cancellationToken = default)
    {
        int months = input.ValueKind == JsonValueKind.Object && input.TryGetProperty("months", out JsonElement value) && value.TryGetInt32(out int given) ? given : 12;
        return AiToolOutput.Data(await analytics.AttritionAsync(months, cancellationToken));
    }
}

/// <summary>Drafts the HR summary of an exit; it is added to the case only when a person approves it.</summary>
internal sealed class DraftExitSummaryTool(SeparationService separations) : IAiActionTool
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Name => "draft_exit_summary";

    public string Description => "Drafts the HR exit summary for one employee's separation (why they left, regretted or not, retention attempted, survey themes, follow-ups) and adds it to the case timeline once approved. Name the employee exactly as in the separation list and write the summary in up to 1,000 characters.";

    public JsonElement InputSchema { get; } = AiJson.Schema(
        ("employeeName", "string", "The leaver's name as shown on their separation", true),
        ("summary", "string", "The exit summary, plain text, up to 1000 characters", true));

    public AiToolKind Kind => AiToolKind.Action;

    public string? Permission => Permissions.Separation.CaseManage;

    public Task<AiToolOutput> RunAsync(JsonElement input, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("draft_exit_summary is an action; it is proposed, not run.");

    public async Task<AiProposal> ProposeAsync(JsonElement input, CancellationToken cancellationToken = default)
    {
        string name = (AiJson.Text(input, "employeeName") ?? string.Empty).Trim();
        string summary = (AiJson.Text(input, "summary") ?? string.Empty).Trim();
        if (summary.Length is 0 or > 1000)
        {
            throw new DomainRuleException("AI_EXIT_SUMMARY", "Write a summary of up to 1,000 characters.");
        }

        List<CaseItem> matches = [.. (await separations.CasesAsync(null, cancellationToken))
            .Where(item => string.Equals(item.EmployeeName, name, StringComparison.OrdinalIgnoreCase) && item.Status is Domain.CaseStatus.Approved or Domain.CaseStatus.Completed)];
        CaseItem item = matches.Count switch
        {
            1 => matches[0],
            0 => throw new DomainRuleException("AI_EXIT_SUMMARY_CASE", $"No approved or completed separation for '{name}' that you manage."),
            _ => throw new DomainRuleException("AI_EXIT_SUMMARY_CASE", $"More than one separation for '{name}'; open the right case and add the summary there."),
        };
        return new AiProposal($"Add an exit summary to {item.EmployeeName}'s separation: \"{(summary.Length > 160 ? summary[..160] + "..." : summary)}\"",
            JsonSerializer.Serialize(new SummaryBody(item.Id, summary), Json));
    }

    public async Task<string> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        SummaryBody body = JsonSerializer.Deserialize<SummaryBody>(payloadJson, Json) ?? throw new DomainRuleException("AI_PAYLOAD", "The proposal could not be read.");
        await separations.AddSummaryAsync(body.CaseId, body.Summary, cancellationToken);
        return "The exit summary was added to the case timeline.";
    }

    private sealed record SummaryBody(Guid CaseId, string Summary);
}
