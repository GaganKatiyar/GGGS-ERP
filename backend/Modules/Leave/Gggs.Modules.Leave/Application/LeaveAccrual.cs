using System.Globalization;
using Gggs.BuildingBlocks.Jobs;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Leave.Domain;
using Gggs.Modules.Leave.Infrastructure;
using Gggs.Modules.Platform.Contracts;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Leave.Application;

public sealed record AccrualResult(string Period, int Accrued, int Lapsed);

/// <summary>
/// Puts days into balances (spec §20 accrual): a twelfth of the entitlement each month for monthly
/// policies, the year's entitlement (prorated for a mid-year joiner) for yearly ones, and at the
/// turn of the year lapses whatever exceeds the carry-forward limit. Every entry carries a period
/// key, so running a month twice - by the schedule and by hand - adds nothing the second time.
/// </summary>
public sealed class LeaveAccrualService(AppDbContext dbContext, IEmployeeDirectory employees, ILogger<LeaveAccrualService> logger)
{
    /// <summary>Joining on or before this day of a month earns that month.</summary>
    private const int MonthCutoffDay = 15;

    /// <param name="employeeId">Only this person - catching up a late joiner - rather than everyone.</param>
    public async Task<AccrualResult> RunAsync(int year, int month, CancellationToken cancellationToken, Guid? employeeId = null)
    {
        if (month is < 1 or > 12 || year is < 2000 or > 2100)
        {
            throw new ArgumentOutOfRangeException(nameof(month), "Choose a real month.");
        }

        var periodStart = new DateOnly(year, month, 1);
        DateOnly periodEnd = periodStart.AddMonths(1).AddDays(-1);

        List<LeavePolicy> policies = await dbContext.LeavePolicies.AsNoTracking()
            .Where(policy => policy.IsActive)
            .Join(dbContext.LeaveTypes.AsNoTracking().Where(type => type.IsActive), policy => policy.LeaveTypeId, type => type.Id, (policy, _) => policy)
            .ToListAsync(cancellationToken);

        if (policies.Count == 0)
        {
            return new AccrualResult(Period(year, month), 0, 0);
        }

        List<EmployeeSummary> people = [];
        IReadOnlyList<Guid> ids = employeeId is { } single ? [single] : await employees.ListActiveIdsAsync(null, null, cancellationToken);
        foreach (Guid[] chunk in ids.Chunk(500))
        {
            people.AddRange(await employees.GetManyAsync(chunk, cancellationToken));
        }

        // The only keys this run can write: this month's accrual, this year's entitlement, last year's lapse.
        List<string?> keys =
        [
            "accrual:" + Period(year, month),
            "accrual:" + year.ToString(CultureInfo.InvariantCulture),
            "lapse:" + (year - 1).ToString(CultureInfo.InvariantCulture),
        ];

        HashSet<(Guid, Guid, string)> existing = [.. (await dbContext.LeaveLedger.AsNoTracking()
                .Where(entry => keys.Contains(entry.PeriodKey))
                .Select(entry => new { entry.EmployeeId, entry.LeaveTypeId, entry.PeriodKey })
                .ToListAsync(cancellationToken))
            .Select(entry => (entry.EmployeeId, entry.LeaveTypeId, entry.PeriodKey!))];

        int lapsed = month == 1 ? await LapseAsync(year - 1, people, policies, existing, cancellationToken) : 0;
        int accrued = 0;

        foreach (EmployeeSummary person in people)
        {
            if (person.JoiningDate is not { } joined || joined > periodEnd)
            {
                continue;
            }

            foreach (Guid typeId in policies.Select(policy => policy.LeaveTypeId).Distinct())
            {
                LeavePolicy? policy = LeaveService.PolicyFor(policies, typeId, person.EmploymentTypeCode);
                if (policy is null || policy.AnnualEntitlement <= 0)
                {
                    continue;
                }

                if (policy.Accrual == AccrualFrequency.Monthly)
                {
                    string key = "accrual:" + Period(year, month);
                    bool earnsMonth = joined < periodStart || joined.Day <= MonthCutoffDay;
                    if (earnsMonth && existing.Add((person.Id, typeId, key)))
                    {
                        dbContext.LeaveLedger.Add(LeaveLedgerEntry.Record(person.Id, typeId, periodStart, LedgerKind.Accrual, Round(policy.AnnualEntitlement / 12), periodKey: key, note: $"Accrual {Period(year, month)}"));
                        accrued++;
                    }
                }
                else if (policy.Accrual == AccrualFrequency.Yearly)
                {
                    string key = "accrual:" + year.ToString(CultureInfo.InvariantCulture);
                    if (!existing.Add((person.Id, typeId, key)))
                    {
                        continue;
                    }

                    // Someone joining this year gets the months from their first full month on.
                    int firstMonth = joined.Year < year ? 1 : (joined.Day <= MonthCutoffDay ? joined.Month : joined.Month + 1);
                    int months = Math.Max(0, 13 - firstMonth);
                    if (months > 0)
                    {
                        dbContext.LeaveLedger.Add(LeaveLedgerEntry.Record(person.Id, typeId, periodStart, LedgerKind.Accrual, Round(policy.AnnualEntitlement * months / 12), periodKey: key, note: $"Entitlement {year}"));
                        accrued++;
                    }
                }
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Leave accrual {Period}: {Accrued} accrued, {Lapsed} lapsed.", Period(year, month), accrued, lapsed);
        return new AccrualResult(Period(year, month), accrued, lapsed);
    }

    /// <summary>At the turn of the year: a balance above the carry-forward limit loses the excess.</summary>
    private async Task<int> LapseAsync(int closingYear, List<EmployeeSummary> people, List<LeavePolicy> policies, HashSet<(Guid, Guid, string)> existing, CancellationToken cancellationToken)
    {
        var yearEnd = new DateOnly(closingYear, 12, 31);
        string key = "lapse:" + closingYear.ToString(CultureInfo.InvariantCulture);
        List<Guid> ids = [.. people.Select(person => person.Id)];

        var balances = await dbContext.LeaveLedger.AsNoTracking()
            .Where(entry => ids.Contains(entry.EmployeeId) && entry.EntryDate <= yearEnd)
            .GroupBy(entry => new { entry.EmployeeId, entry.LeaveTypeId })
            .Select(group => new { group.Key.EmployeeId, group.Key.LeaveTypeId, Balance = group.Sum(entry => entry.Days) })
            .ToListAsync(cancellationToken);

        Dictionary<Guid, EmployeeSummary> byId = people.ToDictionary(person => person.Id);
        int lapsed = 0;

        foreach (var row in balances)
        {
            LeavePolicy? policy = LeaveService.PolicyFor(policies, row.LeaveTypeId, byId[row.EmployeeId].EmploymentTypeCode);
            if (policy?.CarryForwardLimit is not { } limit || row.Balance <= limit || !existing.Add((row.EmployeeId, row.LeaveTypeId, key)))
            {
                continue;
            }

            dbContext.LeaveLedger.Add(LeaveLedgerEntry.Record(row.EmployeeId, row.LeaveTypeId, yearEnd, LedgerKind.Lapse, -(row.Balance - limit), periodKey: key, note: $"Above the carry-forward limit of {limit.ToString("0.#", CultureInfo.InvariantCulture)}"));
            lapsed++;
        }

        return lapsed;
    }

    private static decimal Round(decimal days) => decimal.Round(days, 2, MidpointRounding.AwayFromZero);

    private static string Period(int year, int month) => $"{year:D4}-{month:D2}";
}

/// <summary>On the first of each month, queues one accrual run per active tenant.</summary>
[PlatformJob("Leave accrual: fans out one monthly accrual per active tenant.")]
internal sealed class LeaveAccrualSweep(ITenantFanOutScheduler fanOut)
{
    public const string RecurringJobName = "leave-accrual";

    public Task RunAsync() => fanOut.FanOutAsync<LeaveAccrualJob>((job, tenantId) => job.RunAsync(tenantId));
}

internal sealed class LeaveAccrualJob(LeaveAccrualService accrual, ITenantClock clock)
{
    public async Task RunAsync(Guid tenantId)
    {
        _ = tenantId;
        DateOnly today = await clock.TodayAsync(CancellationToken.None);
        await accrual.RunAsync(today.Year, today.Month, CancellationToken.None);
    }
}

internal sealed class LeaveJobRegistration(IRecurringJobManager recurringJobs) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Early on the first of the month (UTC), daily as a safety net: a run that already happened adds nothing.
        recurringJobs.AddOrUpdate<LeaveAccrualSweep>(LeaveAccrualSweep.RecurringJobName, sweep => sweep.RunAsync(), "30 1 * * *");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed record LeaveTypeSeed(string Code, string Name, bool IsPaid, bool AllowHalfDay, string Color, PolicyRules? Policy);

/// <summary>The leave types, policies and holiday calendar a new tenant starts with; editable after.</summary>
internal static class LeaveCatalog
{
    public static IReadOnlyList<LeaveTypeSeed> Types { get; } =
    [
        new("CL", "Casual leave", IsPaid: true, AllowHalfDay: true, "#1E88E5",
            new PolicyRules(12, AccrualFrequency.Monthly, 0, AllowDuringProbation: true, 0, MaxConsecutiveDays: 3, MinNoticeDays: 0, CountSandwichedDays: false)),
        new("SL", "Sick leave", IsPaid: true, AllowHalfDay: true, "#E53935",
            new PolicyRules(12, AccrualFrequency.Monthly, 0, AllowDuringProbation: true, 0, MaxConsecutiveDays: null, MinNoticeDays: 0, CountSandwichedDays: false)),
        new("EL", "Earned leave", IsPaid: true, AllowHalfDay: false, "#43A047",
            new PolicyRules(18, AccrualFrequency.Monthly, 30, AllowDuringProbation: false, 0, MaxConsecutiveDays: 15, MinNoticeDays: 7, CountSandwichedDays: true)),
        new("LOP", "Loss of pay", IsPaid: false, AllowHalfDay: true, "#757575",
            new PolicyRules(0, AccrualFrequency.None, null, AllowDuringProbation: true, 366, MaxConsecutiveDays: null, MinNoticeDays: 0, CountSandwichedDays: false)),
        new("COMP", "Compensatory off", IsPaid: true, AllowHalfDay: true, "#8E24AA",
            new PolicyRules(0, AccrualFrequency.None, 0, AllowDuringProbation: true, 0, MaxConsecutiveDays: 2, MinNoticeDays: 0, CountSandwichedDays: false)),
    ];
}

/// <summary>Gives the current tenant every catalog leave type, its policy and a default holiday calendar it lacks.</summary>
public sealed class LeaveCatalogSeeder(AppDbContext dbContext, ILogger<LeaveCatalogSeeder> logger)
{
    public async Task<int> SeedCurrentTenantAsync(CancellationToken cancellationToken = default)
    {
        HashSet<string> codes = [.. await dbContext.LeaveTypes
            .IgnoreQueryFilters([Gggs.BuildingBlocks.Persistence.QueryFilters.SoftDelete])
            .Select(type => type.Code)
            .ToListAsync(cancellationToken)];

        int added = 0;
        foreach (LeaveTypeSeed seed in LeaveCatalog.Types.Where(seed => !codes.Contains(seed.Code)))
        {
            var type = LeaveType.Create(seed.Code, seed.Name, seed.IsPaid, seed.AllowHalfDay, seed.Color, isSystem: true);
            dbContext.LeaveTypes.Add(type);
            if (seed.Policy is { } rules)
            {
                dbContext.LeavePolicies.Add(LeavePolicy.Create(type.Id, null, rules));
            }

            added++;
        }

        if (!await dbContext.HolidayCalendars.AnyAsync(cancellationToken))
        {
            dbContext.HolidayCalendars.Add(HolidayCalendar.Create("GENERAL", "General holidays", isDefault: true, []));
            added++;
        }

        if (added > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        logger.LogInformation("Leave catalog seeded: {Added} added.", added);
        return added;
    }
}

internal sealed class SeedLeaveCatalogOnTenantProvisioned(LeaveCatalogSeeder seeder) : IIntegrationEventConsumer<TenantProvisionedIntegrationEvent>
{
    public Task HandleAsync(TenantProvisionedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        => seeder.SeedCurrentTenantAsync(cancellationToken);
}
