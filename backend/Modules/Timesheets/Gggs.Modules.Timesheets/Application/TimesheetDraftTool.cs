using System.Globalization;
using System.Text.Json;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Security;
using Gggs.Modules.Ai.Contracts;
using Gggs.Modules.Project.Contracts;
using Gggs.Modules.Timesheets.Contracts;

namespace Gggs.Modules.Timesheets.Application;

/// <summary>A task to spread time over, and how its hours are counted.</summary>
public sealed record DraftTarget(Guid ProjectId, Guid TaskId, HourCategory Category);

public sealed record DraftDay(DateOnly Date, bool CanBook);

public sealed record DraftLine(DateOnly Date, Guid ProjectId, Guid TaskId, HourCategory Category, decimal Hours);

/// <summary>
/// A first guess at someone's week: a standard day on each day they can book, shared evenly across their tasks.
/// It is a starting point for them to correct, and it never pretends to know what they did.
/// </summary>
public static class TimesheetDraft
{
    public static IReadOnlyList<DraftLine> Build(IReadOnlyList<DraftDay> days, IReadOnlyList<DraftTarget> targets, decimal hoursPerDay)
    {
        ArgumentNullException.ThrowIfNull(days);
        ArgumentNullException.ThrowIfNull(targets);
        if (targets.Count == 0 || hoursPerDay <= 0)
        {
            return [];
        }

        // In quarter hours, so a day adds up exactly; what will not divide goes to the first tasks.
        int quarters = (int)Math.Round(hoursPerDay * 4);
        int each = quarters / targets.Count;
        int over = quarters - (each * targets.Count);
        List<DraftLine> lines = [];
        foreach (DraftDay day in days.Where(day => day.CanBook))
        {
            for (int index = 0; index < targets.Count; index++)
            {
                int share = each + (index < over ? 1 : 0);
                if (share > 0)
                {
                    lines.Add(new DraftLine(day.Date, targets[index].ProjectId, targets[index].TaskId, targets[index].Category, share / 4m));
                }
            }
        }

        return lines;
    }
}

internal sealed record TimesheetDraftPayload(DateOnly Week, IReadOnlyList<DraftLine> Lines);

/// <summary>Drafts this person's week from the tasks assigned to them. It saves a draft, once they approve, and never submits.</summary>
internal sealed class DraftMyTimesheetTool(TimesheetService timesheets) : IAiActionTool
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Name => "draft_my_timesheet";

    public string Description => "Drafts this person's timesheet for a week from the tasks assigned to them, sharing a standard day evenly across those tasks on working days without leave or a holiday. Give the week as any date in it; leave it out for this week. It only starts an empty week, and it stays a draft: the person corrects the hours and submits it themselves. Say plainly that the split is a guess.";

    public JsonElement InputSchema { get; } = AiJson.Schema(("week", "string", "Any date in the week, yyyy-mm-dd. Leave out for this week.", false));

    public AiToolKind Kind => AiToolKind.Action;

    public string? Permission => Permissions.Self.TimesheetSubmit;

    public Task<AiToolOutput> RunAsync(JsonElement input, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("draft_my_timesheet is an action; it is proposed, not run.");

    public async Task<AiProposal> ProposeAsync(JsonElement input, CancellationToken cancellationToken = default)
    {
        TimesheetView view = await timesheets.MyWeekAsync(AiJson.Date(input, "week"), cancellationToken);
        if (view.Entries.Count > 0)
        {
            throw new DomainRuleException("TIMESHEET_NOT_EMPTY", "That week already has hours, so a draft would overwrite them. Correct it in the timesheet instead.");
        }

        List<DraftTarget> targets = [.. view.Targets.SelectMany(target => target.Tasks.Where(task => task.Assigned).Select(task => new DraftTarget(
            target.ProjectId, task.TaskId, target.IsDefault ? HourCategory.Internal : target.Billable ? HourCategory.Billable : HourCategory.NonBillable)))];
        if (targets.Count == 0)
        {
            throw new DomainRuleException("AI_TIMESHEET_NOTHING", "No tasks are assigned to you that week, so there is nothing to draft from.");
        }

        List<DraftDay> days = [.. view.Days.Select(day => new DraftDay(day.Date, day.IsWorkingDay && day.Holiday is null && day.Leave is null))];
        IReadOnlyList<DraftLine> lines = TimesheetDraft.Build(days, targets, view.Rules.StandardHoursPerDay);
        if (lines.Count == 0)
        {
            throw new DomainRuleException("AI_TIMESHEET_NOTHING", "That week has no working days to book, once leave and holidays are left out.");
        }

        decimal total = lines.Sum(line => line.Hours);
        return new AiProposal(
            $"Start your timesheet for the week of {view.Timesheet.WeekStart:d MMM} with {total.ToString("0.##", CultureInfo.InvariantCulture)} h across {targets.Count} assigned tasks. It stays a draft for you to correct and submit.",
            JsonSerializer.Serialize(new TimesheetDraftPayload(view.Timesheet.WeekStart, lines), Json));
    }

    public async Task<string> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        TimesheetDraftPayload payload = JsonSerializer.Deserialize<TimesheetDraftPayload>(payloadJson, Json) ?? throw new DomainRuleException("AI_TIMESHEET", "That proposal could not be read.");

        // The week may have changed since this was drafted: approving must not overwrite what is there now.
        TimesheetView view = await timesheets.MyWeekAsync(payload.Week, cancellationToken);
        if (view.Entries.Count > 0)
        {
            throw new DomainRuleException("TIMESHEET_NOT_EMPTY", "That week has hours in it now, so the draft was not applied.");
        }

        await timesheets.SaveMyWeekAsync(
            payload.Week,
            new WeekBody([.. payload.Lines.Select(line => new EntryBody(line.Date, line.ProjectId, line.TaskId, line.Category, line.Hours, null, null))], null),
            cancellationToken);
        return $"Saved a draft timesheet for the week of {payload.Week:d MMM}. Correct the hours, then submit it from your timesheet.";
    }
}
