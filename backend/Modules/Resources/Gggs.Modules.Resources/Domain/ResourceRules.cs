namespace Gggs.Modules.Resources.Domain;

/// <summary>Capacity states (spec §65).</summary>
public enum CapacityState
{
    Bench = 1,
    PartiallyAvailable = 2,
    FullyAllocated = 3,
    Overallocated = 4,
    Leave = 5,
}

/// <summary>An allocation as the capacity rules see it.</summary>
public sealed record AllocationSpan(Guid ProjectId, string ProjectCode, int Percent, DateOnly Start, DateOnly? End, bool Billable);

/// <summary>One person's capacity over a period (spec §59): allocated, on leave, available, and the state it adds up to.</summary>
public sealed record CapacityCell(
    DateOnly From,
    DateOnly To,
    decimal AllocatedPercent,
    decimal BillablePercent,
    decimal LeavePercent,
    decimal AvailablePercent,
    CapacityState State,
    IReadOnlyList<ProjectShare> Projects);

public sealed record ProjectShare(Guid ProjectId, string ProjectCode, decimal Percent, bool Billable);

public static class Capacity
{
    /// <summary>
    /// Capacity over a period, weighted by working days: an allocation covering half the working days at
    /// 50% contributes 25%. Leave takes its share first; what remains of 100% is available.
    /// </summary>
    public static CapacityCell For(
        DateOnly from,
        DateOnly to,
        IReadOnlyCollection<DateOnly> workingDays,
        IReadOnlyDictionary<DateOnly, decimal> leaveFractions,
        IEnumerable<AllocationSpan> allocations)
    {
        ArgumentNullException.ThrowIfNull(workingDays);
        ArgumentNullException.ThrowIfNull(leaveFractions);
        ArgumentNullException.ThrowIfNull(allocations);

        List<DateOnly> days = [.. workingDays.Where(day => day >= from && day <= to)];
        if (days.Count == 0)
        {
            return new CapacityCell(from, to, 0, 0, 0, 0, CapacityState.Leave, []);
        }

        List<ProjectShare> shares = [.. allocations
            .Select(allocation =>
            {
                int covered = days.Count(day => day >= allocation.Start && (allocation.End is null || day <= allocation.End));
                return new ProjectShare(allocation.ProjectId, allocation.ProjectCode, Math.Round(allocation.Percent * (decimal)covered / days.Count, 1), allocation.Billable);
            })
            .Where(share => share.Percent > 0)
            .GroupBy(share => new { share.ProjectId, share.ProjectCode })
            .Select(group => new ProjectShare(group.Key.ProjectId, group.Key.ProjectCode, group.Sum(share => share.Percent), group.Any(share => share.Billable)))];

        decimal allocated = shares.Sum(share => share.Percent);
        decimal billable = shares.Where(share => share.Billable).Sum(share => share.Percent);
        decimal leave = Math.Round(days.Sum(day => leaveFractions.GetValueOrDefault(day)) * 100m / days.Count, 1);
        decimal available = Math.Max(0, 100 - allocated - leave);

        CapacityState state = leave >= 100 ? CapacityState.Leave
            : allocated > 100 ? CapacityState.Overallocated
            : allocated >= 100 - leave && allocated > 0 ? CapacityState.FullyAllocated
            : allocated > 0 ? CapacityState.PartiallyAvailable
            : CapacityState.Bench;

        return new CapacityCell(from, to, allocated, billable, leave, available, state, shares);
    }

    /// <summary>The busiest a person is on any single day of the window - what a new allocation must fit beside.</summary>
    public static int PeakAllocation(DateOnly from, DateOnly to, IEnumerable<AllocationSpan> allocations)
    {
        ArgumentNullException.ThrowIfNull(allocations);
        List<AllocationSpan> list = [.. allocations];

        // The load only changes where an allocation starts or ends, so those days are enough to check.
        IEnumerable<DateOnly> edges = list.SelectMany(allocation => new[] { allocation.Start, allocation.End?.AddDays(1) ?? to })
            .Append(from)
            .Where(day => day >= from && day <= to)
            .Distinct();

        return edges.Select(day => list.Where(allocation => allocation.Start <= day && (allocation.End is null || allocation.End >= day)).Sum(allocation => allocation.Percent))
            .DefaultIfEmpty(0)
            .Max();
    }
}

/// <summary>What the matcher knows about one candidate.</summary>
public sealed record CandidateFacts(
    Guid EmployeeId,
    IReadOnlyDictionary<Guid, int> SkillLevels,
    IReadOnlyDictionary<Guid, decimal> SkillYears,
    decimal YearsInCompany,
    int PeakAllocationInWindow,
    Guid? LocationId,
    string? WorkModeCode,
    decimal? CostRate,
    bool IsBillable,
    DateOnly? ExitDate);

/// <summary>A candidate's fit for a request, by factor and overall (spec §62).</summary>
public sealed record MatchScore(
    decimal Skills,
    decimal Availability,
    decimal Experience,
    decimal Location,
    decimal Cost,
    decimal Overall,
    IReadOnlyList<string> MissingSkills);

/// <summary>
/// Resource matching (spec §62): skills 40%, availability 25%, experience 15%, location and work mode
/// 10%, cost 10%. Someone leaving before the request ends is only available until they go.
/// </summary>
public static class Matching
{
    public const decimal SkillWeight = 0.40m;
    public const decimal AvailabilityWeight = 0.25m;
    public const decimal ExperienceWeight = 0.15m;
    public const decimal LocationWeight = 0.10m;
    public const decimal CostWeight = 0.10m;

    public static MatchScore Score(ResourceRequest request, CandidateFacts candidate, IReadOnlyDictionary<Guid, string> skillNames)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(skillNames);

        IReadOnlyList<SkillRequirement> required = request.Skills;
        List<string> missing = [];
        decimal skills = 100;
        if (required.Count > 0)
        {
            skills = required.Average(requirement =>
            {
                if (!candidate.SkillLevels.TryGetValue(requirement.SkillId, out int level))
                {
                    missing.Add(skillNames.GetValueOrDefault(requirement.SkillId) ?? "?");
                    return 0m;
                }

                return Math.Min(1m, (decimal)level / requirement.MinLevel) * 100;
            });
        }

        decimal free = Math.Max(0, 100 - candidate.PeakAllocationInWindow);
        decimal availability = Math.Min(1m, free / request.AllocationPercent) * 100;
        if (candidate.ExitDate is { } exit && exit < request.EndDate)
        {
            int span = Math.Max(1, request.EndDate.DayNumber - request.StartDate.DayNumber + 1);
            int stays = Math.Max(0, exit.DayNumber - request.StartDate.DayNumber + 1);
            availability *= Math.Min(1m, (decimal)stays / span);
        }

        decimal years = required.Count == 0
            ? candidate.YearsInCompany
            : required.Select(requirement => candidate.SkillYears.GetValueOrDefault(requirement.SkillId)).DefaultIfEmpty(0).Max();
        decimal experience = request.MinYearsExperience is { } minimum and > 0 ? Math.Min(1m, years / minimum) * 100 : 100;

        decimal place = request.LocationId is null || request.LocationId == candidate.LocationId ? 100 : 40;
        decimal mode = request.WorkModeCode is null || string.Equals(request.WorkModeCode, candidate.WorkModeCode, StringComparison.OrdinalIgnoreCase)
            || string.Equals(request.WorkModeCode, "REMOTE", StringComparison.OrdinalIgnoreCase) ? 100 : 60;
        decimal location = (place + mode) / 2;

        decimal cost = request.MaxCostRate is not { } budget || candidate.CostRate is not { } rate || rate <= budget || rate == 0
            ? 100
            : budget / rate * 100;

        decimal overall = (skills * SkillWeight) + (availability * AvailabilityWeight) + (experience * ExperienceWeight) + (location * LocationWeight) + (cost * CostWeight);
        if (request.Billable && !candidate.IsBillable)
        {
            overall *= 0.8m;
        }

        return new MatchScore(Round(skills), Round(availability), Round(experience), Round(location), Round(cost), Round(overall), missing);
    }

    private static decimal Round(decimal value) => Math.Round(value, 1);
}

/// <summary>Utilization (spec §72): billable hours over available working hours.</summary>
public static class Utilization
{
    public static decimal Percent(decimal billableHours, decimal availableHours)
        => availableHours <= 0 ? 0 : Math.Round(billableHours * 100 / availableHours, 1);
}
