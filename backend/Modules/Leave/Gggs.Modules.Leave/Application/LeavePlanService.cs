using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Leave.Domain;
using Gggs.Modules.Leave.Infrastructure;
using Gggs.Modules.MasterData.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Leave.Application;

public sealed record LeavePlanSummary(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    int LeaveYearStartMonth,
    string LeaveYear,
    bool IsActive,
    int LeaveTypeCount,
    IReadOnlyList<string> AssignedTo,
    int ExceptionCount);

public sealed record LeavePlanRuleItem(
    Guid LeaveTypeId,
    string LeaveTypeCode,
    string LeaveTypeName,
    string Color,
    bool IsPaid,
    bool AllowHalfDay,
    decimal AnnualEntitlement,
    AccrualFrequency Accrual,
    decimal? CarryForwardLimit,
    decimal MaxNegativeBalance,
    bool CountSandwichedDays,
    IReadOnlyList<string> EligibleGenderCodes,
    int MinServiceMonths,
    bool AllowDuringProbation,
    bool AllowDuringNotice,
    decimal? MinDaysPerRequest,
    int? MaxConsecutiveDays,
    int? MaxRequestsPerYear,
    int MinNoticeDays,
    int? MaxBackdatedDays,
    int? MaxAdvanceDays,
    decimal? DocumentRequiredAboveDays,
    CreditTiming CreditTiming,
    int JoiningCutoffDay,
    bool ProrateOnJoining,
    int? CarryForwardExpiryMonths,
    bool EncashAtYearEnd,
    decimal? MaxEncashDays,
    decimal? GrantPerOccurrence,
    int? MaxOccurrencesInEmployment);

public sealed record LeavePlanDetail(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    int LeaveYearStartMonth,
    string LeaveYear,
    bool IsActive,
    IReadOnlyList<string> AssignedTo,
    IReadOnlyList<LeavePlanRuleItem> Items);

public sealed record LeavePlanRequest(string Code, string Name, string? Description, int LeaveYearStartMonth);

public sealed record LeavePlanItemRequest(
    Guid LeaveTypeId,
    decimal AnnualEntitlement,
    AccrualFrequency Accrual,
    decimal? CarryForwardLimit,
    decimal MaxNegativeBalance,
    bool CountSandwichedDays,
    IReadOnlyList<string>? EligibleGenderCodes,
    int MinServiceMonths,
    bool AllowDuringProbation,
    bool AllowDuringNotice,
    decimal? MinDaysPerRequest,
    int? MaxConsecutiveDays,
    int? MaxRequestsPerYear,
    int MinNoticeDays,
    int? MaxBackdatedDays,
    int? MaxAdvanceDays,
    decimal? DocumentRequiredAboveDays = null,
    CreditTiming CreditTiming = CreditTiming.StartOfPeriod,
    int JoiningCutoffDay = 15,
    bool ProrateOnJoining = true,
    int? CarryForwardExpiryMonths = null,
    bool EncashAtYearEnd = false,
    decimal? MaxEncashDays = null,
    decimal? GrantPerOccurrence = null,
    int? MaxOccurrencesInEmployment = null)
{
    public PlanItemRules Rules => new(
        AnnualEntitlement, Accrual, CarryForwardLimit, MaxNegativeBalance, CountSandwichedDays, EligibleGenderCodes ?? [],
        MinServiceMonths, AllowDuringProbation, AllowDuringNotice, MinDaysPerRequest, MaxConsecutiveDays, MaxRequestsPerYear,
        MinNoticeDays, MaxBackdatedDays, MaxAdvanceDays, DocumentRequiredAboveDays, CreditTiming, JoiningCutoffDay == 0 ? 15 : JoiningCutoffDay,
        ProrateOnJoining, CarryForwardExpiryMonths, EncashAtYearEnd, MaxEncashDays, GrantPerOccurrence, MaxOccurrencesInEmployment);
}

public sealed record PlanAssignmentItem(string EmploymentTypeCode, string Label, Guid? PlanId, string? PlanName);

public sealed record PlanAssignmentChange(string EmploymentTypeCode, Guid? PlanId);

public sealed record EmployeePlanItem(
    Guid Id,
    Guid EmployeeId,
    string? EmployeeName,
    string? EmployeeCode,
    Guid PlanId,
    string PlanName,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string? Reason);

public sealed record EmployeePlanRequest(Guid EmployeeId, Guid PlanId, DateOnly EffectiveFrom, DateOnly? EffectiveTo, string? Reason);

/// <summary>The policy an employee follows today, and why: what My leave and Employee 360 show.</summary>
public sealed record ResolvedLeavePolicy(
    Guid PlanId,
    string Code,
    string Name,
    string LeaveYear,
    DateOnly YearStart,
    DateOnly YearEnd,
    PlanSource Source,
    IReadOnlyList<LeavePlanRuleItem> Items);

/// <summary>Every policy, assignment and exception a calculation needs, loaded once.</summary>
public sealed class LeavePlanBook(
    IReadOnlyDictionary<Guid, LeavePlan> plans,
    IReadOnlyDictionary<string, Guid> assignments,
    ILookup<Guid, EmployeeLeavePlan> exceptions)
{
    public bool HasPlans => plans.Count > 0;

    public IEnumerable<LeavePlan> Plans => plans.Values;

    /// <summary>The active policy the person follows on that day, or null when none is assigned.</summary>
    public (LeavePlan Plan, PlanSource Source)? For(EmployeeSummary person, DateOnly day)
    {
        ArgumentNullException.ThrowIfNull(person);
        PlanChoice? choice = LeavePlanResolver.Resolve(exceptions[person.Id], assignments, person.EmploymentTypeCode, day);
        return choice is not null && plans.TryGetValue(choice.PlanId, out LeavePlan? plan) && plan.IsActive ? (plan, choice.Source) : null;
    }
}

/// <summary>
/// Leave policies (spec §20): named sets of leave types and rules, assigned per engagement type,
/// with per-employee exceptions. Reads build a <see cref="LeavePlanBook"/>; HR edits go through the
/// methods below, each checking what would leave someone without a policy.
/// </summary>
public sealed class LeavePlanService(AppDbContext dbContext, IEmployeeDirectory employees, IMasterDataLookup lookup)
{
    public async Task<LeavePlanBook> BookAsync(IReadOnlyCollection<Guid>? employeeIds, CancellationToken cancellationToken)
    {
        Dictionary<Guid, LeavePlan> plans = await dbContext.LeavePlans.AsNoTracking().Include(plan => plan.Items).ToDictionaryAsync(plan => plan.Id, cancellationToken);
        Dictionary<string, Guid> assignments = await dbContext.LeavePlanAssignments.AsNoTracking()
            .ToDictionaryAsync(assignment => assignment.EmploymentTypeCode, assignment => assignment.PlanId, StringComparer.OrdinalIgnoreCase, cancellationToken);

        IQueryable<EmployeeLeavePlan> exceptions = dbContext.EmployeeLeavePlans.AsNoTracking().Where(exception => exception.IsActive);
        if (employeeIds is not null)
        {
            List<Guid> ids = [.. employeeIds];
            exceptions = exceptions.Where(exception => ids.Contains(exception.EmployeeId));
        }

        return new LeavePlanBook(plans, assignments, (await exceptions.ToListAsync(cancellationToken)).ToLookup(exception => exception.EmployeeId));
    }

    public async Task<ResolvedLeavePolicy?> ResolveAsync(Guid employeeId, DateOnly day, CancellationToken cancellationToken)
    {
        EmployeeSummary person = await employees.GetAsync(employeeId, cancellationToken) ?? throw new NotFoundException("Employee", employeeId);
        LeavePlanBook book = await BookAsync([employeeId], cancellationToken);
        if (book.For(person, day) is not { } resolved)
        {
            return null;
        }

        LeaveYear year = LeaveYear.For(resolved.Plan.LeaveYearStartMonth, day);
        IReadOnlyList<LeavePlanRuleItem> items = await RuleItemsAsync(resolved.Plan, cancellationToken);
        return new ResolvedLeavePolicy(resolved.Plan.Id, resolved.Plan.Code, resolved.Plan.Name, LeaveYear.Label(resolved.Plan.LeaveYearStartMonth), year.Start, year.End, resolved.Source, items);
    }

    // ----- Policies ------------------------------------------------------------------------------

    public async Task<IReadOnlyList<LeavePlanSummary>> ListAsync(CancellationToken cancellationToken)
    {
        List<LeavePlan> plans = await dbContext.LeavePlans.AsNoTracking().Include(plan => plan.Items).OrderBy(plan => plan.Name).ToListAsync(cancellationToken);
        ILookup<Guid, string> assigned = (await dbContext.LeavePlanAssignments.AsNoTracking().ToListAsync(cancellationToken))
            .ToLookup(assignment => assignment.PlanId, assignment => assignment.EmploymentTypeCode);
        Dictionary<Guid, int> exceptions = await dbContext.EmployeeLeavePlans.AsNoTracking()
            .Where(exception => exception.IsActive)
            .GroupBy(exception => exception.PlanId)
            .ToDictionaryAsync(group => group.Key, group => group.Count(), cancellationToken);

        return [.. plans.Select(plan => new LeavePlanSummary(
            plan.Id, plan.Code, plan.Name, plan.Description, plan.LeaveYearStartMonth, LeaveYear.Label(plan.LeaveYearStartMonth), plan.IsActive,
            plan.Items.Count, [.. assigned[plan.Id].Order(StringComparer.Ordinal)], exceptions.GetValueOrDefault(plan.Id)))];
    }

    public async Task<LeavePlanDetail> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        LeavePlan plan = await dbContext.LeavePlans.AsNoTracking().Include(row => row.Items).FirstOrDefaultAsync(row => row.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(LeavePlan), id);
        List<string> assignedTo = await dbContext.LeavePlanAssignments.AsNoTracking()
            .Where(assignment => assignment.PlanId == id)
            .Select(assignment => assignment.EmploymentTypeCode)
            .OrderBy(code => code)
            .ToListAsync(cancellationToken);

        return new LeavePlanDetail(
            plan.Id, plan.Code, plan.Name, plan.Description, plan.LeaveYearStartMonth, LeaveYear.Label(plan.LeaveYearStartMonth), plan.IsActive,
            assignedTo, await RuleItemsAsync(plan, cancellationToken));
    }

    public async Task<LeavePlanDetail> CreateAsync(LeavePlanRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        string code = LeavePlan.NormalizeCode(request.Code);
        await EnsureCodeFreeAsync(code, cancellationToken);

        var plan = LeavePlan.Create(code, request.Name, request.Description, request.LeaveYearStartMonth);
        dbContext.LeavePlans.Add(plan);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await GetAsync(plan.Id, cancellationToken);
    }

    public async Task<LeavePlanDetail> UpdateAsync(Guid id, LeavePlanRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        LeavePlan plan = await dbContext.LeavePlans.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(LeavePlan), id);
        plan.Update(request.Name, request.Description, request.LeaveYearStartMonth);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>Replaces the policy's leave types and their rules in one save.</summary>
    public async Task<LeavePlanDetail> SetItemsAsync(Guid id, IReadOnlyList<LeavePlanItemRequest> items, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        LeavePlan plan = await dbContext.LeavePlans.Include(row => row.Items).FirstOrDefaultAsync(row => row.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(LeavePlan), id);

        List<Guid> typeIds = [.. items.Select(item => item.LeaveTypeId).Distinct()];
        int known = await dbContext.LeaveTypes.CountAsync(type => typeIds.Contains(type.Id), cancellationToken);
        if (known != typeIds.Count)
        {
            throw new DomainRuleException("LEAVE_TYPE_UNKNOWN", "Every leave type in the policy must exist.");
        }

        foreach (string gender in items.SelectMany(item => item.EligibleGenderCodes ?? []).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!await lookup.IsValidAsync(LookupLists.Gender, gender, cancellationToken))
            {
                throw new DomainRuleException("LEAVE_POLICY_GENDER", $"'{gender}' is not a gender in master data.");
            }
        }

        HashSet<Guid> existing = [.. plan.Items.Select(item => item.Id)];
        IReadOnlyList<LeavePlanItem> removed = plan.SetItems([.. items.Select(item => (item.LeaveTypeId, item.Rules))]);
        dbContext.LeavePlanItems.RemoveRange(removed);

        // New items carry their key already, so change detection would take them for existing rows:
        // say outright that they are new, so they are inserted and stamped with the tenant.
        foreach (LeavePlanItem added in plan.Items.Where(item => !existing.Contains(item.Id)))
        {
            dbContext.Entry(added).State = EntityState.Added;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>A new, inactive-until-assigned copy to start a similar policy from.</summary>
    public async Task<LeavePlanDetail> CopyAsync(Guid id, CancellationToken cancellationToken)
    {
        LeavePlan source = await dbContext.LeavePlans.AsNoTracking().Include(row => row.Items).FirstOrDefaultAsync(row => row.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(LeavePlan), id);

        string code = source.Code.Length > 26 ? source.Code[..26] : source.Code;
        string candidate = code + "_COPY";
        for (int number = 2; await dbContext.LeavePlans.AnyAsync(plan => plan.Code == candidate, cancellationToken); number++)
        {
            candidate = $"{code}_{number}";
        }

        string name = $"Copy of {source.Name}";
        var copy = LeavePlan.Create(candidate, name.Length > 100 ? name[..100] : name, source.Description, source.LeaveYearStartMonth);
        copy.SetItems([.. source.Items.Select(item => (item.LeaveTypeId, item.Rules))]);
        dbContext.LeavePlans.Add(copy);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await GetAsync(copy.Id, cancellationToken);
    }

    public async Task SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken)
    {
        LeavePlan plan = await dbContext.LeavePlans.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(LeavePlan), id);
        if (!active && (await dbContext.LeavePlanAssignments.AnyAsync(assignment => assignment.PlanId == id, cancellationToken)
                || await dbContext.EmployeeLeavePlans.AnyAsync(exception => exception.PlanId == id && exception.IsActive, cancellationToken)))
        {
            throw new DomainRuleException("LEAVE_PLAN_IN_USE", "This policy is assigned to an engagement type or an employee. Assign another policy first.");
        }

        if (active)
        {
            plan.Activate();
        }
        else
        {
            plan.Deactivate();
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    // ----- Assignment ----------------------------------------------------------------------------

    /// <summary>Every engagement type in master data and the policy it follows, if any.</summary>
    public async Task<IReadOnlyList<PlanAssignmentItem>> AssignmentsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<LookupOption> types = await lookup.ValuesAsync(LookupLists.EmploymentType, cancellationToken);
        Dictionary<string, Guid> assigned = await dbContext.LeavePlanAssignments.AsNoTracking()
            .ToDictionaryAsync(assignment => assignment.EmploymentTypeCode, assignment => assignment.PlanId, StringComparer.OrdinalIgnoreCase, cancellationToken);
        Dictionary<Guid, string> names = await dbContext.LeavePlans.AsNoTracking().ToDictionaryAsync(plan => plan.Id, plan => plan.Name, cancellationToken);

        return [.. types.Select(type =>
        {
            Guid? planId = assigned.TryGetValue(type.Code, out Guid id) ? id : null;
            return new PlanAssignmentItem(type.Code, type.Label, planId, planId is { } some ? names.GetValueOrDefault(some) : null);
        })];
    }

    public async Task<IReadOnlyList<PlanAssignmentItem>> SetAssignmentsAsync(IReadOnlyList<PlanAssignmentChange> changes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changes);
        Dictionary<string, LeavePlanAssignment> current = await dbContext.LeavePlanAssignments
            .ToDictionaryAsync(assignment => assignment.EmploymentTypeCode, StringComparer.OrdinalIgnoreCase, cancellationToken);
        HashSet<Guid> activePlans = [.. await dbContext.LeavePlans.Where(plan => plan.IsActive).Select(plan => plan.Id).ToListAsync(cancellationToken)];

        foreach (PlanAssignmentChange change in changes)
        {
            string code = change.EmploymentTypeCode.Trim().ToUpperInvariant();
            if (!await lookup.IsValidAsync(LookupLists.EmploymentType, code, cancellationToken))
            {
                throw new DomainRuleException("LEAVE_ASSIGNMENT_TYPE", $"'{code}' is not an engagement type in master data.");
            }

            if (change.PlanId is not { } planId)
            {
                if (current.Remove(code, out LeavePlanAssignment? gone))
                {
                    dbContext.LeavePlanAssignments.Remove(gone);
                }

                continue;
            }

            if (!activePlans.Contains(planId))
            {
                throw new DomainRuleException("LEAVE_PLAN_INACTIVE", "Assign an active leave policy.");
            }

            if (current.TryGetValue(code, out LeavePlanAssignment? existing))
            {
                existing.Reassign(planId);
            }
            else
            {
                LeavePlanAssignment assignment = LeavePlanAssignment.Create(code, planId);
                dbContext.LeavePlanAssignments.Add(assignment);
                current[code] = assignment;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await AssignmentsAsync(cancellationToken);
    }

    // ----- Exceptions ----------------------------------------------------------------------------

    public async Task<IReadOnlyList<EmployeePlanItem>> ExceptionsAsync(Guid? employeeId, CancellationToken cancellationToken)
    {
        List<EmployeeLeavePlan> rows = await dbContext.EmployeeLeavePlans.AsNoTracking()
            .Where(exception => exception.IsActive && (employeeId == null || exception.EmployeeId == employeeId))
            .OrderByDescending(exception => exception.EffectiveFrom)
            .ToListAsync(cancellationToken);
        Dictionary<Guid, string> plans = await dbContext.LeavePlans.AsNoTracking().ToDictionaryAsync(plan => plan.Id, plan => plan.Name, cancellationToken);
        Dictionary<Guid, EmployeeSummary> people = (await employees.GetManyAsync(rows.Select(row => row.EmployeeId), cancellationToken)).ToDictionary(person => person.Id);

        return [.. rows.Select(row => new EmployeePlanItem(
            row.Id, row.EmployeeId, people.GetValueOrDefault(row.EmployeeId)?.DisplayName, people.GetValueOrDefault(row.EmployeeId)?.EmployeeCode,
            row.PlanId, plans.GetValueOrDefault(row.PlanId) ?? string.Empty, row.EffectiveFrom, row.EffectiveTo, row.Reason))];
    }

    public async Task<Guid> AddExceptionAsync(EmployeePlanRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await EnsureExceptionValidAsync(request, exceptId: null, cancellationToken);
        var exception = EmployeeLeavePlan.Create(request.EmployeeId, request.PlanId, request.EffectiveFrom, request.EffectiveTo, request.Reason);
        dbContext.EmployeeLeavePlans.Add(exception);
        await dbContext.SaveChangesAsync(cancellationToken);
        return exception.Id;
    }

    public async Task UpdateExceptionAsync(Guid id, EmployeePlanRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        EmployeeLeavePlan exception = await dbContext.EmployeeLeavePlans.FirstOrDefaultAsync(row => row.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(EmployeeLeavePlan), id);
        if (exception.EmployeeId != request.EmployeeId)
        {
            throw new DomainRuleException("LEAVE_EXCEPTION_EMPLOYEE", "An exception stays with its employee; remove it and add another.");
        }

        await EnsureExceptionValidAsync(request, exceptId: id, cancellationToken);
        exception.Update(request.PlanId, request.EffectiveFrom, request.EffectiveTo, request.Reason);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveExceptionAsync(Guid id, CancellationToken cancellationToken)
    {
        EmployeeLeavePlan exception = await dbContext.EmployeeLeavePlans.FirstOrDefaultAsync(row => row.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(EmployeeLeavePlan), id);
        dbContext.EmployeeLeavePlans.Remove(exception);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureExceptionValidAsync(EmployeePlanRequest request, Guid? exceptId, CancellationToken cancellationToken)
    {
        if (await employees.GetAsync(request.EmployeeId, cancellationToken) is null)
        {
            throw new NotFoundException("Employee", request.EmployeeId);
        }

        if (!await dbContext.LeavePlans.AnyAsync(plan => plan.Id == request.PlanId && plan.IsActive, cancellationToken))
        {
            throw new DomainRuleException("LEAVE_PLAN_INACTIVE", "Choose an active leave policy.");
        }

        List<EmployeeLeavePlan> others = await dbContext.EmployeeLeavePlans.AsNoTracking()
            .Where(exception => exception.EmployeeId == request.EmployeeId && exception.IsActive && exception.Id != exceptId)
            .ToListAsync(cancellationToken);
        if (others.Any(other => other.Overlaps(request.EffectiveFrom, request.EffectiveTo)))
        {
            throw new DomainRuleException("LEAVE_EXCEPTION_OVERLAP", "This employee already has a policy exception for some of those dates.");
        }
    }

    private async Task EnsureCodeFreeAsync(string code, CancellationToken cancellationToken)
    {
        if (await dbContext.LeavePlans.AnyAsync(plan => plan.Code == code, cancellationToken))
        {
            throw new DomainRuleException("LEAVE_PLAN_CODE_IN_USE", $"A leave policy with code '{code}' already exists.");
        }
    }

    private async Task<IReadOnlyList<LeavePlanRuleItem>> RuleItemsAsync(LeavePlan plan, CancellationToken cancellationToken)
    {
        List<Guid> typeIds = [.. plan.Items.Select(item => item.LeaveTypeId)];
        Dictionary<Guid, LeaveType> types = await dbContext.LeaveTypes.AsNoTracking().Where(type => typeIds.Contains(type.Id)).ToDictionaryAsync(type => type.Id, cancellationToken);

        return [.. plan.Items
            .Where(item => types.ContainsKey(item.LeaveTypeId))
            .Select(item => (item, type: types[item.LeaveTypeId]))
            .OrderBy(pair => pair.type.Name)
            .Select(pair => ToRuleItem(pair.item, pair.type))];
    }

    internal static LeavePlanRuleItem ToRuleItem(LeavePlanItem item, LeaveType type) => new(
        type.Id, type.Code, type.Name, type.Color, type.IsPaid, type.AllowHalfDay,
        item.AnnualEntitlement, item.Accrual, item.CarryForwardLimit, item.MaxNegativeBalance, item.CountSandwichedDays,
        item.Genders, item.MinServiceMonths, item.AllowDuringProbation, item.AllowDuringNotice,
        item.MinDaysPerRequest, item.MaxConsecutiveDays, item.MaxRequestsPerYear, item.MinNoticeDays, item.MaxBackdatedDays, item.MaxAdvanceDays,
        item.DocumentRequiredAboveDays, item.CreditTiming, item.JoiningCutoffDay, item.ProrateOnJoining, item.CarryForwardExpiryMonths,
        item.EncashAtYearEnd, item.MaxEncashDays, item.GrantPerOccurrence, item.MaxOccurrencesInEmployment);
}
