using System.Globalization;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Jobs;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Leave.Domain;
using Gggs.Modules.Leave.Infrastructure;
using Gggs.Modules.MasterData.Contracts;
using Gggs.Modules.Payroll.Contracts;
using Gggs.Modules.Platform.Contracts;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Leave.Application;

public sealed record AccrualResult(string Period, int Accrued, int Lapsed);

/// <summary>
/// Puts days into balances (spec §20 accrual) by each employee's leave policy - monthly, quarterly,
/// half-yearly or yearly, at the start of each period or once it has ended (<see cref="AccrualSchedule"/>) -
/// and closes leave years: the excess over the carry-forward limit is encashed or lapses, and
/// carried days expire when the policy says. Every entry carries a period key, so running a month
/// twice - by the schedule and by hand - adds nothing the second time.
/// </summary>
public sealed class LeaveAccrualService(
    AppDbContext dbContext,
    IEmployeeDirectory employees,
    LeavePlanService plans,
    IPayrollInputs payrollInputs,
    IPayrollSalaries salaries,
    ILogger<LeaveAccrualService> logger)
{
    /// <summary>Where year-end encashment inputs come from, so payroll can trace them back.</summary>
    public const string EncashmentSource = "leave.encashment";

    /// <param name="employeeId">Only this person - catching up a late joiner - rather than everyone.</param>
    public async Task<AccrualResult> RunAsync(int year, int month, CancellationToken cancellationToken, Guid? employeeId = null)
    {
        if (month is < 1 or > 12 || year is < 2000 or > 2100)
        {
            throw new ArgumentOutOfRangeException(nameof(month), "Choose a real month.");
        }

        var periodStart = new DateOnly(year, month, 1);
        DateOnly closingDay = periodStart.AddDays(-1);

        LeavePlanBook book = await plans.BookAsync(employeeId is { } only ? [only] : null, cancellationToken);
        if (!book.HasPlans)
        {
            return new AccrualResult(Period(year, month), 0, 0);
        }

        HashSet<Guid> activeTypes = [.. await dbContext.LeaveTypes.AsNoTracking().Where(type => type.IsActive).Select(type => type.Id).ToListAsync(cancellationToken)];

        List<EmployeeSummary> people = [];
        IReadOnlyList<Guid> ids = employeeId is { } single ? [single] : await employees.ListActiveIdsAsync(null, null, cancellationToken);
        foreach (Guid[] chunk in ids.Chunk(500))
        {
            people.AddRange(await employees.GetManyAsync(chunk, cancellationToken));
        }

        // Every credit this run could write, worked out first, so one query finds those already written.
        var credits = new List<(Guid EmployeeId, Guid LeaveTypeId, AccrualCredit Credit)>();
        foreach (EmployeeSummary person in people)
        {
            if (person.JoiningDate is not { } joined || joined > periodStart.AddMonths(1).AddDays(-1))
            {
                continue;
            }

            if (book.For(person, joined > periodStart ? joined : periodStart) is not { } resolved)
            {
                continue;
            }

            foreach (LeavePlanItem item in resolved.Plan.Items.Where(item => activeTypes.Contains(item.LeaveTypeId)))
            {
                if (AccrualSchedule.For(item.Rules, resolved.Plan.LeaveYearStartMonth, joined, periodStart) is { } credit)
                {
                    credits.Add((person.Id, item.LeaveTypeId, credit));
                }
            }
        }

        HashSet<string> keys = [.. credits.Select(row => row.Credit.PeriodKey)];
        foreach (LeavePlan plan in book.Plans)
        {
            string closed = LeaveYear.For(plan.LeaveYearStartMonth, closingDay).Key;
            keys.Add("lapse:" + closed);
            keys.Add("encash:" + closed);
            keys.Add("expiry:" + closed);
        }

        List<string?> keyList = [.. keys];
        HashSet<(Guid, Guid, string)> existing = [.. (await dbContext.LeaveLedger.AsNoTracking()
                .Where(entry => keyList.Contains(entry.PeriodKey))
                .Select(entry => new { entry.EmployeeId, entry.LeaveTypeId, entry.PeriodKey })
                .ToListAsync(cancellationToken))
            .Select(entry => (entry.EmployeeId, entry.LeaveTypeId, entry.PeriodKey!))];

        int lapsed = await CloseYearAsync(month, closingDay, people, book, existing, cancellationToken);
        lapsed += await ExpireCarriedAsync(periodStart, people, book, existing, cancellationToken);

        int accrued = 0;
        foreach ((Guid person, Guid typeId, AccrualCredit credit) in credits)
        {
            if (existing.Add((person, typeId, credit.PeriodKey)))
            {
                dbContext.LeaveLedger.Add(LeaveLedgerEntry.Record(person, typeId, credit.EntryDate, LedgerKind.Accrual, credit.Days, periodKey: credit.PeriodKey, note: credit.Note));
                accrued++;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Leave accrual {Period}: {Accrued} accrued, {Lapsed} lapsed or expired.", Period(year, month), accrued, lapsed);
        return new AccrualResult(Period(year, month), accrued, lapsed);
    }

    /// <summary>
    /// For everyone whose policy's leave year starts this month: the balance on the last day of the
    /// year just closed above the carry-forward limit is encashed (when the policy pays out, through
    /// payroll at basic over 26 days) and the rest of the excess lapses.
    /// </summary>
    private async Task<int> CloseYearAsync(int month, DateOnly closingDay, List<EmployeeSummary> people, LeavePlanBook book, HashSet<(Guid, Guid, string)> existing, CancellationToken cancellationToken)
    {
        Dictionary<Guid, LeavePlan> closing = [];
        foreach (EmployeeSummary person in people)
        {
            if (book.For(person, closingDay) is { } resolved && resolved.Plan.LeaveYearStartMonth == month)
            {
                closing[person.Id] = resolved.Plan;
            }
        }

        if (closing.Count == 0)
        {
            return 0;
        }

        List<Guid> ids = [.. closing.Keys];
        var balances = await dbContext.LeaveLedger.AsNoTracking()
            .Where(entry => ids.Contains(entry.EmployeeId) && entry.EntryDate <= closingDay)
            .GroupBy(entry => new { entry.EmployeeId, entry.LeaveTypeId })
            .Select(group => new { group.Key.EmployeeId, group.Key.LeaveTypeId, Balance = group.Sum(entry => entry.Days) })
            .ToListAsync(cancellationToken);

        IReadOnlyDictionary<Guid, SalaryInfo>? pay = null;
        int closed = 0;
        foreach (var row in balances)
        {
            LeavePlan plan = closing[row.EmployeeId];
            if (plan.ItemFor(row.LeaveTypeId) is not { } item)
            {
                continue;
            }

            string year = LeaveYear.For(plan.LeaveYearStartMonth, closingDay).Key;
            YearEndOutcome outcome = YearEnd.Close(item.Rules, row.Balance);

            if (outcome.Encashed > 0 && existing.Add((row.EmployeeId, row.LeaveTypeId, "encash:" + year)))
            {
                pay ??= await salaries.CurrentAsync(ids, closingDay, cancellationToken);
                LeaveLedgerEntry encashment = LeaveLedgerEntry.Record(row.EmployeeId, row.LeaveTypeId, closingDay, LedgerKind.Encashment, -outcome.Encashed, periodKey: "encash:" + year,
                    note: pay.ContainsKey(row.EmployeeId) ? $"Encashed at the end of {year}" : $"Encashed at the end of {year}; no salary on record, so settle it by hand");
                dbContext.LeaveLedger.Add(encashment);

                if (pay.TryGetValue(row.EmployeeId, out SalaryInfo? salary))
                {
                    decimal amount = decimal.Round(salary.MonthlyBasic / 26m * outcome.Encashed, 2, MidpointRounding.AwayFromZero);
                    try
                    {
                        await payrollInputs.AddAsync(
                            new PayrollInputRequest(row.EmployeeId, closingDay.AddDays(1), PayrollInputKind.LeaveEncashmentAnnual, amount,
                                $"Leave encashment: {outcome.Encashed.ToString("0.#", CultureInfo.InvariantCulture)} day(s) of {year}", EncashmentSource, encashment.Id),
                            cancellationToken);
                    }
                    catch (DomainRuleException exception)
                    {
                        // The days are still encashed; payroll can pay them once it is ready.
                        logger.LogWarning("Leave encashment for {EmployeeId} was not sent to payroll: {Reason}", row.EmployeeId, exception.Message);
                    }
                }

                closed++;
            }

            if (outcome.Lapsed > 0 && existing.Add((row.EmployeeId, row.LeaveTypeId, "lapse:" + year)))
            {
                dbContext.LeaveLedger.Add(LeaveLedgerEntry.Record(row.EmployeeId, row.LeaveTypeId, closingDay, LedgerKind.Lapse, -outcome.Lapsed, periodKey: "lapse:" + year,
                    note: $"Above the carry-forward limit of {item.CarryForwardLimit?.ToString("0.#", CultureInfo.InvariantCulture)}"));
                closed++;
            }
        }

        return closed;
    }

    /// <summary>
    /// For policies whose carried days expire this many months into the leave year: what was carried
    /// in and has not been taken since lapses now.
    /// </summary>
    private async Task<int> ExpireCarriedAsync(DateOnly periodStart, List<EmployeeSummary> people, LeavePlanBook book, HashSet<(Guid, Guid, string)> existing, CancellationToken cancellationToken)
    {
        var due = new List<(Guid EmployeeId, LeavePlanItem Item, LeaveYear Year)>();
        foreach (EmployeeSummary person in people)
        {
            if (book.For(person, periodStart) is not { } resolved)
            {
                continue;
            }

            LeaveYear year = LeaveYear.For(resolved.Plan.LeaveYearStartMonth, periodStart);
            due.AddRange(resolved.Plan.Items
                .Where(item => item.CarryForwardExpiryMonths is { } months && year.Start.AddMonths(months) == periodStart)
                .Select(item => (person.Id, item, year)));
        }

        if (due.Count == 0)
        {
            return 0;
        }

        List<Guid> ids = [.. due.Select(row => row.EmployeeId).Distinct()];
        var entries = await dbContext.LeaveLedger.AsNoTracking()
            .Where(entry => ids.Contains(entry.EmployeeId) && entry.EntryDate < periodStart)
            .Select(entry => new { entry.EmployeeId, entry.LeaveTypeId, entry.EntryDate, entry.Kind, entry.Days })
            .ToListAsync(cancellationToken);

        int expired = 0;
        foreach ((Guid employeeId, LeavePlanItem item, LeaveYear year) in due)
        {
            var mine = entries.Where(entry => entry.EmployeeId == employeeId && entry.LeaveTypeId == item.LeaveTypeId).ToList();
            decimal carried = mine.Where(entry => entry.EntryDate < year.Start).Sum(entry => entry.Days);
            decimal taken = -mine.Where(entry => entry.EntryDate >= year.Start && entry.Kind is LedgerKind.Taken or LedgerKind.Restored).Sum(entry => entry.Days);
            decimal balance = mine.Sum(entry => entry.Days);
            decimal expiring = YearEnd.Expiring(carried, taken, balance);

            string previous = LeaveYear.For(year.Start.Month, year.Start.AddDays(-1)).Key;
            if (expiring > 0 && existing.Add((employeeId, item.LeaveTypeId, "expiry:" + previous)))
            {
                dbContext.LeaveLedger.Add(LeaveLedgerEntry.Record(employeeId, item.LeaveTypeId, periodStart.AddDays(-1), LedgerKind.Lapse, -expiring, periodKey: "expiry:" + previous,
                    note: $"Days carried from {previous} not used within {item.CarryForwardExpiryMonths} month(s)"));
                expired++;
            }
        }

        return expired;
    }

    private static string Period(int year, int month) => $"{year:D4}-{month:D2}";
}

/// <summary>On the first of each month, queues one accrual run per active tenant.</summary>
[PlatformJob("Leave accrual: fans out one monthly accrual per active tenant.")]
internal sealed class LeaveAccrualSweep(ITenantFanOutScheduler fanOut)
{
    public const string RecurringJobName = "leave-accrual";

    public Task RunAsync() => fanOut.FanOutAsync<LeaveAccrualJob>((job, tenantId) => job.RunAsync(tenantId));
}

internal sealed class LeaveAccrualJob(LeaveAccrualService accrual, LeaveCatalogSeeder seeder, ITenantClock clock)
{
    public async Task RunAsync(Guid tenantId)
    {
        _ = tenantId;

        // A tenant still on the rules from before named policies is converted before anything accrues.
        await seeder.EnsurePoliciesAsync(CancellationToken.None);
        DateOnly today = await clock.TodayAsync(CancellationToken.None);
        await accrual.RunAsync(today.Year, today.Month, CancellationToken.None);
    }
}

/// <summary>Once at start-up: gives every tenant the catalog's new leave types and converts any still on the rules from before named policies.</summary>
[PlatformJob("Leave policies: converts each tenant's old per-type leave rules into named policies, once.")]
internal sealed class LeavePlanUpgradeSweep(ITenantFanOutScheduler fanOut)
{
    public Task RunAsync() => fanOut.FanOutAsync<LeavePlanUpgradeJob>((job, tenantId) => job.RunAsync(tenantId));
}

internal sealed class LeavePlanUpgradeJob(LeaveCatalogSeeder seeder)
{
    public async Task RunAsync(Guid tenantId)
    {
        _ = tenantId;

        // Catalog types added since (maternity, paternity) arrive too; existing policies are left for HR to extend.
        await seeder.SeedCurrentTenantAsync(CancellationToken.None);
    }
}

internal sealed class LeaveJobRegistration(IRecurringJobManager recurringJobs, IBackgroundJobClient backgroundJobs) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Early on the first of the month (UTC), daily as a safety net: a run that already happened adds nothing.
        recurringJobs.AddOrUpdate<LeaveAccrualSweep>(LeaveAccrualSweep.RecurringJobName, sweep => sweep.RunAsync(), "30 1 * * *");

        // Does nothing for a tenant that already has policies, so queuing it at every start is harmless.
        backgroundJobs.Enqueue<LeavePlanUpgradeSweep>(sweep => sweep.RunAsync());
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed record LeaveTypeSeed(string Code, string Name, bool IsPaid, bool AllowHalfDay, string Color, PlanItemRules Rules);

/// <summary>The leave types, the standard policy and the holiday calendar a new tenant starts with; editable after.</summary>
internal static class LeaveCatalog
{
    public const string StandardPlanCode = "STANDARD";

    /// <summary>
    /// The engagement types master data starts every tenant with. The standard policy is assigned to
    /// these as well as to whatever the tenant's list holds, because at provisioning the list may not
    /// be seeded yet.
    /// </summary>
    public static IReadOnlyList<string> DefaultEngagementTypes { get; } = ["FULL_TIME", "PART_TIME", "CONTRACT", "CONSULTANT", "INTERN", "TEMPORARY", "APPRENTICE"];

    public static IReadOnlyList<LeaveTypeSeed> Types { get; } =
    [
        new("CL", "Casual leave", IsPaid: true, AllowHalfDay: true, "#1E88E5", Rules(12, AccrualFrequency.Monthly, 0, allowDuringProbation: true, 0, longest: 3, notice: 0, sandwich: false)),
        new("SL", "Sick leave", IsPaid: true, AllowHalfDay: true, "#E53935", Rules(12, AccrualFrequency.Monthly, 0, allowDuringProbation: true, 0, longest: null, notice: 0, sandwich: false)),
        new("EL", "Earned leave", IsPaid: true, AllowHalfDay: false, "#43A047", Rules(18, AccrualFrequency.Monthly, 30, allowDuringProbation: false, 0, longest: 15, notice: 7, sandwich: true)),
        new("LOP", "Loss of pay", IsPaid: false, AllowHalfDay: true, "#757575", Rules(0, AccrualFrequency.None, null, allowDuringProbation: true, 366, longest: null, notice: 0, sandwich: false)),
        new("COMP", "Compensatory off", IsPaid: true, AllowHalfDay: true, "#8E24AA", Rules(0, AccrualFrequency.None, 0, allowDuringProbation: true, 0, longest: 2, notice: 0, sandwich: false)),
        new("ML", "Maternity leave", IsPaid: true, AllowHalfDay: false, "#D81B60", Rules(0, AccrualFrequency.None, null, allowDuringProbation: true, 0, longest: null, notice: 0, sandwich: false) with
        {
            EligibleGenderCodes = ["FEMALE"],
            GrantPerOccurrence = 182,
            MaxOccurrencesInEmployment = 2,
        }),
        new("PL", "Paternity leave", IsPaid: true, AllowHalfDay: false, "#3949AB", Rules(0, AccrualFrequency.None, null, allowDuringProbation: true, 0, longest: null, notice: 0, sandwich: false) with
        {
            EligibleGenderCodes = ["MALE"],
            GrantPerOccurrence = 5,
            MaxOccurrencesInEmployment = 2,
        }),
    ];

    /// <summary>The rules the policies before named ones could express; everything newer starts open.</summary>
    public static PlanItemRules Rules(decimal entitlement, AccrualFrequency accrual, decimal? carryForward, bool allowDuringProbation, decimal maxNegative, int? longest, int notice, bool sandwich)
        => new(entitlement, accrual, carryForward, maxNegative, sandwich, [], 0, allowDuringProbation, AllowDuringNotice: true, null, longest, null, notice, null, null);
}

/// <summary>
/// Gives the current tenant every catalog leave type and a default holiday calendar it lacks, and
/// makes sure it has leave policies: a tenant with none gets a standard policy built from its old
/// per-type rules (or the catalog's, when it has none), assigned to every engagement type.
/// </summary>
public sealed class LeaveCatalogSeeder(AppDbContext dbContext, IMasterDataLookup lookup, ILogger<LeaveCatalogSeeder> logger)
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
            dbContext.LeaveTypes.Add(LeaveType.Create(seed.Code, seed.Name, seed.IsPaid, seed.AllowHalfDay, seed.Color, isSystem: true));
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

        added += await EnsurePoliciesAsync(cancellationToken);
        logger.LogInformation("Leave catalog seeded: {Added} added.", added);
        return added;
    }

    /// <summary>
    /// For a tenant without leave policies: "Standard leave policy" from the old general rules, one
    /// more per engagement type that had rules of its own, and each engagement type assigned. Returns
    /// the number of policies created; a tenant that has any does nothing.
    /// </summary>
    public async Task<int> EnsurePoliciesAsync(CancellationToken cancellationToken = default)
    {
        if (await dbContext.LeavePlans.IgnoreQueryFilters([Gggs.BuildingBlocks.Persistence.QueryFilters.SoftDelete]).AnyAsync(cancellationToken))
        {
            return 0;
        }

        List<LeaveType> types = await dbContext.LeaveTypes.AsNoTracking().Where(type => type.IsActive).ToListAsync(cancellationToken);
        List<LeavePolicy> legacy = await dbContext.LeavePolicies.AsNoTracking().Where(policy => policy.IsActive).ToListAsync(cancellationToken);
        HashSet<Guid> typeIds = [.. types.Select(type => type.Id)];

        Dictionary<Guid, PlanItemRules> standard = legacy.Count > 0
            ? legacy.Where(policy => policy.EmploymentTypeCode is null && typeIds.Contains(policy.LeaveTypeId)).ToDictionary(policy => policy.LeaveTypeId, FromLegacy)
            : types.Join(LeaveCatalog.Types, type => type.Code, seed => seed.Code, (type, seed) => (type.Id, seed.Rules)).ToDictionary(pair => pair.Id, pair => pair.Rules);

        var standardPlan = LeavePlan.Create(
            LeaveCatalog.StandardPlanCode,
            "Standard leave policy",
            legacy.Count > 0 ? "Made from the leave rules in place before named policies." : "The policy every engagement type starts on. Copy it to make others.",
            leaveYearStartMonth: 1);
        standardPlan.SetItems([.. standard.Select(pair => (pair.Key, pair.Value))]);
        dbContext.LeavePlans.Add(standardPlan);

        // An engagement type that had rules of its own keeps them: the standard rules, overridden by its own.
        Dictionary<string, LeavePlan> own = new(StringComparer.OrdinalIgnoreCase);
        List<IGrouping<string, LeavePolicy>> specific = [.. legacy.Where(policy => policy.EmploymentTypeCode is not null && typeIds.Contains(policy.LeaveTypeId)).GroupBy(policy => policy.EmploymentTypeCode!)];
        IReadOnlyDictionary<string, string> labels = await lookup.GetLabelsAsync(LookupLists.EmploymentType, specific.Select(group => group.Key), cancellationToken);
        foreach (IGrouping<string, LeavePolicy> group in specific)
        {
            Dictionary<Guid, PlanItemRules> rules = new(standard);
            foreach (LeavePolicy policy in group)
            {
                rules[policy.LeaveTypeId] = FromLegacy(policy);
            }

            string label = labels.GetValueOrDefault(group.Key) ?? group.Key;
            var plan = LeavePlan.Create(group.Key, $"{label} leave policy", "Made from the leave rules in place before named policies.", leaveYearStartMonth: 1);
            plan.SetItems([.. rules.Select(pair => (pair.Key, pair.Value))]);
            dbContext.LeavePlans.Add(plan);
            own[group.Key] = plan;
        }

        IReadOnlyList<LookupOption> engagementTypes = await lookup.ValuesAsync(LookupLists.EmploymentType, cancellationToken);
        IEnumerable<string> everyType = engagementTypes.Select(option => option.Code)
            .Concat(LeaveCatalog.DefaultEngagementTypes)
            .Concat(own.Keys)
            .Select(code => code.Trim().ToUpperInvariant())
            .Distinct();
        foreach (string code in everyType)
        {
            dbContext.LeavePlanAssignments.Add(LeavePlanAssignment.Create(code, own.TryGetValue(code, out LeavePlan? plan) ? plan.Id : standardPlan.Id));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Leave policies created: {Count} (from {Legacy} old rule(s)).", own.Count + 1, legacy.Count);
        return own.Count + 1;
    }

    private static PlanItemRules FromLegacy(LeavePolicy policy) => LeaveCatalog.Rules(
        policy.AnnualEntitlement, policy.Accrual, policy.CarryForwardLimit, policy.AllowDuringProbation, policy.MaxNegativeBalance,
        policy.MaxConsecutiveDays, policy.MinNoticeDays, policy.CountSandwichedDays);
}

internal sealed class SeedLeaveCatalogOnTenantProvisioned(LeaveCatalogSeeder seeder) : IIntegrationEventConsumer<TenantProvisionedIntegrationEvent>
{
    public Task HandleAsync(TenantProvisionedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        => seeder.SeedCurrentTenantAsync(cancellationToken);
}
