using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Organization.Contracts;
using Gggs.Modules.Project.Contracts;
using Gggs.Modules.Resources.Domain;
using Gggs.Modules.Resources.Infrastructure;
using Gggs.Modules.Skills.Contracts;
using Gggs.Modules.Timesheets.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Resources.Application;

public enum UtilizationGrouping
{
    Employee = 1,
    Project = 2,
    Department = 3,
    Manager = 4,
    Location = 5,
}

public sealed record UtilizationRow(
    Guid? Key,
    string Name,
    decimal AvailableHours,
    decimal BillableHours,
    decimal LoggedHours,
    decimal UtilizationPercent,
    IReadOnlyDictionary<string, decimal> HoursByCategory);

public sealed record UtilizationTrendPoint(DateOnly Month, decimal AvailableHours, decimal BillableHours, decimal UtilizationPercent);

public sealed record UtilizationReport(
    DateOnly From,
    DateOnly To,
    UtilizationGrouping GroupBy,
    decimal AvailableHours,
    decimal BillableHours,
    decimal UtilizationPercent,
    IReadOnlyDictionary<string, decimal> HoursByCategory,
    IReadOnlyList<UtilizationRow> Rows,
    IReadOnlyList<UtilizationTrendPoint> Trend);

public sealed record ForecastCell(DateOnly Month, int Demand, int Supply, int Gap);

public sealed record ForecastRow(Guid SkillId, string SkillName, IReadOnlyList<ForecastCell> Months);

public sealed record CapacitySummaryRow(DateOnly Month, int People, decimal AllocatedFte, decimal AvailableFte, decimal LeaveFte, int OnBench, int Overallocated);

public sealed record ForecastReport(IReadOnlyList<ForecastRow> Skills, IReadOnlyList<CapacitySummaryRow> Capacity, int PeopleLeaving);

/// <summary>
/// Utilization (spec §72-73) from approved timesheets against working hours, and the demand-supply
/// forecast (spec §64-66) from open requests against the capacity people will have.
/// </summary>
internal sealed class ResourceInsights(
    AppDbContext dbContext,
    ResourceService resources,
    IProjectDirectory projects,
    ISkillDirectory skills,
    ITimesheetLedger ledger,
    IOrgStructure orgStructure,
    IEmployeeDirectory employees,
    WorkingCalendar calendar,
    ITenantClock clock)
{
    /// <summary>Someone counts towards supply when at least this share of them is free that month.</summary>
    public const decimal SupplyThreshold = 50;

    public async Task<UtilizationReport> UtilizationAsync(DateOnly? from, DateOnly? to, UtilizationGrouping groupBy, Guid? departmentId, CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        DateOnly start = from ?? new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        DateOnly end = to ?? new DateOnly(today.Year, today.Month, 1).AddDays(-1);
        if (end < start)
        {
            (start, end) = (end, start);
        }

        List<EmployeeSummary> people = await resources.PeopleAsync(departmentId, null, null, cancellationToken);
        Dictionary<Guid, ResourceProfile> profiles = await resources.ProfilesAsync([.. people.Select(person => person.Id)], cancellationToken);
        people = [.. people.Where(person => profiles.GetValueOrDefault(person.Id)?.IsBillable ?? true)];
        List<Guid> ids = [.. people.Select(person => person.Id)];

        DateOnly trendStart = new DateOnly(end.Year, end.Month, 1).AddMonths(-5);
        DateOnly windowStart = trendStart < start ? trendStart : start;
        IReadOnlyDictionary<Guid, PersonCalendar> calendars = await calendar.ForAsync(ids, windowStart, end, cancellationToken);
        IReadOnlyList<LoggedHours> hours = await ledger.HoursAsync(ids, windowStart, end, approvedOnly: true, cancellationToken);

        decimal Available(Guid employeeId, DateOnly first, DateOnly last)
            => (calendars.GetValueOrDefault(employeeId)?.AvailableDays(first, last) ?? 0) * (profiles.GetValueOrDefault(employeeId)?.HoursPerDay ?? 8);

        List<LoggedHours> inWindow = [.. hours.Where(row => row.Date >= start && row.Date <= end)];
        decimal totalAvailable = ids.Sum(id => Available(id, start, end));
        decimal totalBillable = inWindow.Where(row => row.Category == HourCategory.Billable).Sum(row => row.Hours);

        IReadOnlyList<UtilizationRow> rows = groupBy switch
        {
            UtilizationGrouping.Project => await ByProjectAsync(inWindow, cancellationToken),
            _ => await ByPeopleAsync(people, inWindow, groupBy, id => Available(id, start, end), cancellationToken),
        };

        List<UtilizationTrendPoint> trend = [.. WorkingCalendar.Months(trendStart, end).Select(month =>
        {
            decimal available = ids.Sum(id => Available(id, month.From, month.To));
            decimal billable = hours.Where(row => row.Date >= month.From && row.Date <= month.To && row.Category == HourCategory.Billable).Sum(row => row.Hours);
            return new UtilizationTrendPoint(month.From, available, billable, Utilization.Percent(billable, available));
        })];

        return new UtilizationReport(start, end, groupBy, totalAvailable, totalBillable, Utilization.Percent(totalBillable, totalAvailable), ByCategory(inWindow), rows, trend);
    }

    /// <summary>Demand against supply by skill and month (spec §64, §66), and capacity by month (spec §65).</summary>
    public async Task<ForecastReport> ForecastAsync(int months, CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        DateOnly from = new(today.Year, today.Month, 1);
        DateOnly to = from.AddMonths(Math.Clamp(months, 1, 12)).AddDays(-1);
        IReadOnlyList<(DateOnly From, DateOnly To)> monthList = WorkingCalendar.Months(from, to);

        List<ResourceRequest> requests = await dbContext.ResourceRequests.AsNoTracking()
            .Where(request => (request.Status == RequestStatus.Open || request.Status == RequestStatus.PartiallyFulfilled) && request.StartDate <= to && request.EndDate >= from)
            .ToListAsync(cancellationToken);

        List<EmployeeSummary> people = await resources.PeopleAsync(null, null, null, cancellationToken);
        List<Guid> ids = [.. people.Select(person => person.Id)];
        IReadOnlyDictionary<Guid, PersonCalendar> calendars = await calendar.ForAsync(ids, from, to, cancellationToken);
        ILookup<Guid, ProjectAllocation> allocations = (await projects.AllocationsAsync(ids, from, to, cancellationToken)).Where(row => row.IsActive).ToLookup(row => row.EmployeeId);
        IReadOnlyList<EmployeeSkillLevel> levels = await skills.LevelsAsync(ids, cancellationToken);
        Dictionary<Guid, DateOnly?> exits = people.ToDictionary(person => person.Id, person => person.ExitDate);

        // Each person's capacity per month, once.
        Dictionary<(Guid, DateOnly), CapacityCell> cells = [];
        foreach (Guid id in ids)
        {
            PersonCalendar? personCalendar = calendars.GetValueOrDefault(id);
            List<AllocationSpan> spans = [.. allocations[id].Select(ResourceService.Span)];
            foreach ((DateOnly monthFrom, DateOnly monthTo) in monthList)
            {
                bool gone = exits[id] is { } exit && exit < monthFrom;
                cells[(id, monthFrom)] = gone
                    ? new CapacityCell(monthFrom, monthTo, 0, 0, 100, 0, CapacityState.Leave, [])
                    : Capacity.For(monthFrom, monthTo, personCalendar?.WorkingDays ?? [], personCalendar?.LeaveFractions ?? new Dictionary<DateOnly, decimal>(), spans);
            }
        }

        // Demand names the skills; supply counts holders of each at the level asked for, with room that month.
        List<Guid> demanded = [.. requests.SelectMany(request => request.Skills.Select(skill => skill.SkillId)).Distinct()];
        IReadOnlyDictionary<Guid, SkillInfo> skillInfo = await skills.GetSkillsAsync(demanded, cancellationToken);
        ILookup<Guid, EmployeeSkillLevel> holders = levels.ToLookup(level => level.SkillId);

        List<ForecastRow> skillRows = [.. demanded.Select(skillId =>
        {
            int minimum = requests.SelectMany(request => request.Skills).Where(skill => skill.SkillId == skillId).Select(skill => skill.MinLevel).DefaultIfEmpty(1).Min();
            List<Guid> able = [.. holders[skillId].Where(level => level.Level >= minimum).Select(level => level.EmployeeId).Distinct()];
            return new ForecastRow(
                skillId,
                skillInfo.GetValueOrDefault(skillId)?.Name ?? "?",
                [.. monthList.Select(month =>
                {
                    int demand = requests
                        .Where(request => request.StartDate <= month.To && request.EndDate >= month.From && request.Skills.Any(skill => skill.SkillId == skillId))
                        .Sum(request => request.Remaining);
                    int supply = able.Count(id => cells.TryGetValue((id, month.From), out CapacityCell? cell) && cell.AvailablePercent >= SupplyThreshold);
                    return new ForecastCell(month.From, demand, supply, demand - supply);
                })]);
        }).OrderByDescending(row => row.Months.Sum(cell => cell.Gap))];

        List<CapacitySummaryRow> capacity = [.. monthList.Select(month =>
        {
            List<CapacityCell> monthCells = [.. ids.Select(id => cells[(id, month.From)])];
            return new CapacitySummaryRow(
                month.From,
                ids.Count(id => exits[id] is not { } exit || exit >= month.From),
                Math.Round(monthCells.Sum(cell => cell.AllocatedPercent) / 100, 1),
                Math.Round(monthCells.Sum(cell => cell.AvailablePercent) / 100, 1),
                Math.Round(monthCells.Sum(cell => cell.LeavePercent) / 100, 1),
                monthCells.Count(cell => cell.State == CapacityState.Bench),
                monthCells.Count(cell => cell.State == CapacityState.Overallocated));
        })];

        return new ForecastReport(skillRows, capacity, people.Count(person => person.ExitDate is { } exit && exit >= today && exit <= to));
    }

    // ----- Internals ----------------------------------------------------------------------------

    private static Dictionary<string, decimal> ByCategory(IEnumerable<LoggedHours> hours)
        => hours.GroupBy(row => row.Category.ToString()).ToDictionary(group => group.Key, group => group.Sum(row => row.Hours));

    private async Task<IReadOnlyList<UtilizationRow>> ByPeopleAsync(
        List<EmployeeSummary> people,
        List<LoggedHours> hours,
        UtilizationGrouping groupBy,
        Func<Guid, decimal> available,
        CancellationToken cancellationToken)
    {
        ILookup<Guid, LoggedHours> byPerson = hours.ToLookup(row => row.EmployeeId);
        Func<EmployeeSummary, Guid?> key = groupBy switch
        {
            UtilizationGrouping.Department => person => person.DepartmentId,
            UtilizationGrouping.Manager => person => person.ManagerEmployeeId,
            UtilizationGrouping.Location => person => person.LocationId,
            _ => person => person.Id,
        };

        IReadOnlyDictionary<Guid, string> names = groupBy switch
        {
            UtilizationGrouping.Department => await orgStructure.GetNamesAsync(OrgUnitKind.Department, people.Select(person => person.DepartmentId).OfType<Guid>().Distinct(), cancellationToken),
            UtilizationGrouping.Location => await orgStructure.GetNamesAsync(OrgUnitKind.Location, people.Select(person => person.LocationId).OfType<Guid>().Distinct(), cancellationToken),
            UtilizationGrouping.Manager => (await employees.GetManyAsync(people.Select(person => person.ManagerEmployeeId).OfType<Guid>().Distinct(), cancellationToken)).ToDictionary(person => person.Id, person => person.DisplayName),
            _ => people.ToDictionary(person => person.Id, person => person.DisplayName),
        };

        return [.. people
            .GroupBy(key)
            .Select(group =>
            {
                List<LoggedHours> logged = [.. group.SelectMany(person => byPerson[person.Id])];
                decimal availableHours = group.Sum(person => available(person.Id));
                decimal billable = logged.Where(row => row.Category == HourCategory.Billable).Sum(row => row.Hours);
                return new UtilizationRow(
                    group.Key, group.Key is { } id ? names.GetValueOrDefault(id) ?? "?" : "Not set", availableHours, billable, logged.Sum(row => row.Hours),
                    Utilization.Percent(billable, availableHours), ByCategory(logged));
            })
            .OrderByDescending(row => row.UtilizationPercent)];
    }

    /// <summary>For projects, utilization is the billable share of the hours booked to it.</summary>
    private async Task<IReadOnlyList<UtilizationRow>> ByProjectAsync(List<LoggedHours> hours, CancellationToken cancellationToken)
    {
        List<LoggedHours> onProjects = [.. hours.Where(row => row.ProjectId is not null)];
        IReadOnlyDictionary<Guid, ProjectSummary> names = await projects.GetManyAsync(onProjects.Select(row => row.ProjectId!.Value), cancellationToken);
        return [.. onProjects
            .GroupBy(row => row.ProjectId!.Value)
            .Select(group =>
            {
                decimal logged = group.Sum(row => row.Hours);
                decimal billable = group.Where(row => row.Category == HourCategory.Billable).Sum(row => row.Hours);
                ProjectSummary? project = names.GetValueOrDefault(group.Key);
                return new UtilizationRow(group.Key, project is null ? "?" : $"{project.Code} {project.Name}", logged, billable, logged, Utilization.Percent(billable, logged), ByCategory(group));
            })
            .OrderByDescending(row => row.LoggedHours)];
    }
}
