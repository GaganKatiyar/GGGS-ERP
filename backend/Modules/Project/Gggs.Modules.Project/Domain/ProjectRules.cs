namespace Gggs.Modules.Project.Domain;

/// <summary>The facts a project's health is judged from, gathered by the service.</summary>
public sealed record HealthInputs(
    DateOnly Today,
    DateOnly StartDate,
    DateOnly TargetEndDate,
    decimal ProgressPercent,
    int MeasuredTaskCount,
    IReadOnlyList<int> MilestoneDaysOverdue,
    int MilestoneCount,
    int ActiveTeamCount,
    bool HasProjectManager,
    int MembersEndingSoon,
    int PendingChangeRequests,
    int ApprovedScheduleImpactDays,
    int OpenBugs,
    int OpenHighBugs,
    int OpenCriticalBugs,
    int MaxOpenRiskScore,
    int OpenRisks,
    int OpenCriticalIssues,
    int OpenHighIssues,
    int OverdueIssues,
    byte? ClientSatisfaction);

public sealed record HealthResult(
    HealthRating Schedule,
    HealthRating Budget,
    HealthRating Resources,
    HealthRating Scope,
    HealthRating Quality,
    HealthRating Risk,
    HealthRating Issues,
    HealthRating ClientSatisfaction,
    HealthRating Overall,
    decimal Score,
    decimal ProgressPercent,
    IReadOnlyList<string> Reasons);

/// <summary>
/// Project health (spec §78): each dimension rated green, amber or red against the tenant's
/// thresholds, then combined into an overall verdict.
/// <para>
/// Overall is a weighted average, not the worst dimension: green counts 0, amber 1 and red 3, so
/// one red among otherwise healthy dimensions reads amber (the spec's own example), while two red
/// dimensions always read red however healthy the rest. Dimensions with nothing to judge are left out rather than
/// counted as green. Budget waits for project financials (M7), which know what has been spent.
/// </para>
/// </summary>
public static class HealthCalculator
{
    public static HealthResult Assess(HealthInputs inputs, ProjectHealthThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(thresholds);
        List<string> reasons = [];

        HealthRating schedule = Schedule(inputs, thresholds, reasons);
        HealthRating budget = HealthRating.NotAssessed;
        reasons.Add("Budget: not assessed until project financials record actual cost.");
        HealthRating resources = Resources(inputs, reasons);
        HealthRating scope = Scope(inputs, thresholds, reasons);
        HealthRating quality = Quality(inputs, thresholds, reasons);
        HealthRating risk = Risk(inputs, thresholds, reasons);
        HealthRating issues = Issues(inputs, thresholds, reasons);
        HealthRating client = Client(inputs, reasons);

        HealthRating[] assessed = [.. new[] { schedule, budget, resources, scope, quality, risk, issues, client }.Where(rating => rating != HealthRating.NotAssessed)];
        decimal score = assessed.Length == 0 ? 0 : assessed.Average(Weight);
        HealthRating overall = assessed.Length == 0
            ? HealthRating.NotAssessed
            : score >= thresholds.OverallRedScore || assessed.Count(rating => rating == HealthRating.Red) >= 2 ? HealthRating.Red
            : score >= thresholds.OverallAmberScore ? HealthRating.Amber
            : HealthRating.Green;

        return new HealthResult(schedule, budget, resources, scope, quality, risk, issues, client, overall, score, inputs.ProgressPercent, reasons);
    }

    /// <summary>How far through its planned time the project is, 0-100.</summary>
    public static decimal ElapsedPercent(DateOnly today, DateOnly start, DateOnly end)
    {
        if (today <= start)
        {
            return 0;
        }

        int span = Math.Max(1, end.DayNumber - start.DayNumber);
        return Math.Clamp((today.DayNumber - start.DayNumber) * 100m / span, 0, 100);
    }

    private static decimal Weight(HealthRating rating) => rating switch
    {
        HealthRating.Amber => 1,
        HealthRating.Red => 3,
        _ => 0,
    };

    private static HealthRating Schedule(HealthInputs inputs, ProjectHealthThresholds thresholds, List<string> reasons)
    {
        if (inputs.MeasuredTaskCount == 0 && inputs.MilestoneCount == 0)
        {
            reasons.Add("Schedule: no tasks or milestones to measure yet.");
            return HealthRating.NotAssessed;
        }

        int worstOverdue = inputs.MilestoneDaysOverdue.Count == 0 ? 0 : inputs.MilestoneDaysOverdue.Max();
        decimal gap = inputs.MeasuredTaskCount == 0 ? 0 : ElapsedPercent(inputs.Today, inputs.StartDate, inputs.TargetEndDate) - inputs.ProgressPercent;

        if (worstOverdue >= thresholds.MilestoneRedOverdueDays || gap >= thresholds.ScheduleRedGap)
        {
            reasons.Add(worstOverdue >= thresholds.MilestoneRedOverdueDays
                ? $"Schedule: a milestone is {worstOverdue} days late."
                : $"Schedule: progress trails elapsed time by {gap:0} points.");
            return HealthRating.Red;
        }

        if (worstOverdue > 0 || gap >= thresholds.ScheduleAmberGap)
        {
            reasons.Add(worstOverdue > 0
                ? $"Schedule: {inputs.MilestoneDaysOverdue.Count(days => days > 0)} milestone(s) late."
                : $"Schedule: progress trails elapsed time by {gap:0} points.");
            return HealthRating.Amber;
        }

        reasons.Add("Schedule: on track.");
        return HealthRating.Green;
    }

    private static HealthRating Resources(HealthInputs inputs, List<string> reasons)
    {
        if (inputs.ActiveTeamCount == 0)
        {
            reasons.Add("Resources: nobody is on the team.");
            return HealthRating.Red;
        }

        if (!inputs.HasProjectManager || inputs.MembersEndingSoon > 0)
        {
            reasons.Add(!inputs.HasProjectManager
                ? "Resources: no project manager is named."
                : $"Resources: {inputs.MembersEndingSoon} team member(s) leave the project soon.");
            return HealthRating.Amber;
        }

        reasons.Add($"Resources: {inputs.ActiveTeamCount} on the team.");
        return HealthRating.Green;
    }

    private static HealthRating Scope(HealthInputs inputs, ProjectHealthThresholds thresholds, List<string> reasons)
    {
        if (inputs.PendingChangeRequests >= thresholds.ScopeRedPendingChanges || inputs.ApprovedScheduleImpactDays >= thresholds.ScopeRedScheduleImpactDays)
        {
            reasons.Add($"Scope: {inputs.PendingChangeRequests} change(s) awaiting a decision, {inputs.ApprovedScheduleImpactDays} day(s) added by approved changes.");
            return HealthRating.Red;
        }

        if (inputs.PendingChangeRequests >= thresholds.ScopeAmberPendingChanges || inputs.ApprovedScheduleImpactDays > 0)
        {
            reasons.Add($"Scope: {inputs.PendingChangeRequests} change(s) awaiting a decision, {inputs.ApprovedScheduleImpactDays} day(s) added by approved changes.");
            return HealthRating.Amber;
        }

        reasons.Add("Scope: stable.");
        return HealthRating.Green;
    }

    private static HealthRating Quality(HealthInputs inputs, ProjectHealthThresholds thresholds, List<string> reasons)
    {
        if (inputs.OpenCriticalBugs > 0 || inputs.OpenBugs >= thresholds.QualityRedOpenBugs)
        {
            reasons.Add($"Quality: {inputs.OpenBugs} open bug(s), {inputs.OpenCriticalBugs} critical.");
            return HealthRating.Red;
        }

        if (inputs.OpenHighBugs > 0 || inputs.OpenBugs >= thresholds.QualityAmberOpenBugs)
        {
            reasons.Add($"Quality: {inputs.OpenBugs} open bug(s), {inputs.OpenHighBugs} high priority.");
            return HealthRating.Amber;
        }

        reasons.Add($"Quality: {inputs.OpenBugs} open bug(s).");
        return HealthRating.Green;
    }

    private static HealthRating Risk(HealthInputs inputs, ProjectHealthThresholds thresholds, List<string> reasons)
    {
        if (inputs.MaxOpenRiskScore >= thresholds.RiskRedScore)
        {
            reasons.Add($"Risk: an open risk scores {inputs.MaxOpenRiskScore} of 25.");
            return HealthRating.Red;
        }

        if (inputs.MaxOpenRiskScore >= thresholds.RiskAmberScore)
        {
            reasons.Add($"Risk: the highest open risk scores {inputs.MaxOpenRiskScore} of 25.");
            return HealthRating.Amber;
        }

        reasons.Add($"Risk: {inputs.OpenRisks} open risk(s), none high.");
        return HealthRating.Green;
    }

    private static HealthRating Issues(HealthInputs inputs, ProjectHealthThresholds thresholds, List<string> reasons)
    {
        if (inputs.OpenCriticalIssues > 0 || inputs.OverdueIssues >= thresholds.IssueRedOverdueCount)
        {
            reasons.Add($"Issues: {inputs.OpenCriticalIssues} critical, {inputs.OverdueIssues} overdue.");
            return HealthRating.Red;
        }

        if (inputs.OpenHighIssues > 0 || inputs.OverdueIssues > 0)
        {
            reasons.Add($"Issues: {inputs.OpenHighIssues} high severity, {inputs.OverdueIssues} overdue.");
            return HealthRating.Amber;
        }

        reasons.Add("Issues: under control.");
        return HealthRating.Green;
    }

    private static HealthRating Client(HealthInputs inputs, List<string> reasons)
    {
        if (inputs.ClientSatisfaction is not { } score)
        {
            reasons.Add("Client satisfaction: not rated yet.");
            return HealthRating.NotAssessed;
        }

        reasons.Add($"Client satisfaction: {score} of 5.");
        return score >= 4 ? HealthRating.Green : score == 3 ? HealthRating.Amber : HealthRating.Red;
    }
}

/// <summary>Task dependencies as a graph, to refuse the one that would close a loop.</summary>
public static class DependencyGraph
{
    /// <summary>
    /// Whether adding "<paramref name="successor"/> waits on <paramref name="predecessor"/>" would create
    /// a cycle: true when the predecessor already (transitively) waits on the successor.
    /// </summary>
    public static bool WouldCreateCycle(IEnumerable<(Guid Predecessor, Guid Successor)> existing, Guid predecessor, Guid successor)
    {
        ArgumentNullException.ThrowIfNull(existing);
        if (predecessor == successor)
        {
            return true;
        }

        // Walk forward from the new successor: reaching the new predecessor means the edge would close a loop.
        ILookup<Guid, Guid> next = existing.ToLookup(edge => edge.Predecessor, edge => edge.Successor);
        var seen = new HashSet<Guid>();
        var pending = new Stack<Guid>();
        pending.Push(successor);

        while (pending.Count > 0)
        {
            Guid current = pending.Pop();
            if (current == predecessor)
            {
                return true;
            }

            if (!seen.Add(current))
            {
                continue;
            }

            foreach (Guid following in next[current])
            {
                pending.Push(following);
            }
        }

        return false;
    }
}

public sealed record BurndownPoint(DateOnly Date, decimal Ideal, int? Remaining);

/// <summary>A sprint's burndown (spec §48): story points left at the end of each day, against the straight line.</summary>
public static class Burndown
{
    public static IReadOnlyList<BurndownPoint> Compute(
        DateOnly start,
        DateOnly end,
        DateOnly today,
        IEnumerable<(int Points, DateOnly? CompletedOn)> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        List<(int Points, DateOnly? CompletedOn)> work = [.. tasks];
        int total = work.Sum(task => task.Points);
        int days = Math.Max(1, end.DayNumber - start.DayNumber);

        List<BurndownPoint> points = [];
        for (DateOnly day = start; day <= end; day = day.AddDays(1))
        {
            decimal ideal = Math.Round(total * (1 - ((day.DayNumber - start.DayNumber) / (decimal)days)), 1);
            int? remaining = day > today
                ? null
                : total - work.Where(task => task.CompletedOn is { } done && done <= day).Sum(task => task.Points);
            points.Add(new BurndownPoint(day, Math.Max(0, ideal), remaining));
        }

        return points;
    }
}

/// <summary>How far along a set of tasks is, weighted by the best measure the team uses.</summary>
public static class ProgressMeter
{
    /// <summary>
    /// Story points when the team estimates in points, else estimated hours, else each task counts
    /// the same. Epics are left out: their children carry the work.
    /// </summary>
    public static (decimal Percent, int Measured) Of(IEnumerable<(TaskType Type, int? Points, decimal? Hours, int Progress, bool Done)> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        List<(TaskType Type, int? Points, decimal? Hours, int Progress, bool Done)> work = [.. tasks.Where(task => task.Type != TaskType.Epic)];
        if (work.Count == 0)
        {
            return (0, 0);
        }

        Func<(TaskType Type, int? Points, decimal? Hours, int Progress, bool Done), decimal> weight =
            work.Any(task => task.Points > 0) ? task => task.Points ?? 0
            : work.Any(task => task.Hours > 0) ? task => task.Hours ?? 0
            : _ => 1;

        decimal total = work.Sum(weight);
        if (total == 0)
        {
            return (0, work.Count);
        }

        decimal done = work.Sum(task => weight(task) * (task.Done ? 100 : task.Progress) / 100m);
        return (Math.Round(done * 100 / total, 1), work.Count);
    }
}
