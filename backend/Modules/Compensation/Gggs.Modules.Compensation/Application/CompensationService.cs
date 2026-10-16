using System.Globalization;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Benefits.Contracts;
using Gggs.Modules.Compensation.Domain;
using Gggs.Modules.Compensation.Infrastructure;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Notification.Contracts;
using Gggs.Modules.Organization.Contracts;
using Gggs.Modules.Payroll.Contracts;
using Gggs.Modules.Workflow.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Compensation.Application;

public sealed record BandItem(
    Guid Id,
    Guid GradeId,
    string? GradeName,
    string Currency,
    decimal Minimum,
    decimal Midpoint,
    decimal Maximum,
    DateOnly EffectiveFrom,
    string? Notes,
    bool Current,
    int Employees,
    decimal? AverageCompaRatio,
    int BelowRange,
    int AboveRange);

public sealed record BandBody(Guid GradeId, string? Currency, decimal Minimum, decimal Midpoint, decimal Maximum, DateOnly EffectiveFrom, string? Notes);

public sealed record CycleItem(
    Guid Id,
    string Name,
    DateOnly EffectiveDate,
    decimal BudgetPercent,
    CycleStatus Status,
    string? Notes,
    decimal Budget,
    decimal Committed,
    decimal Proposed,
    int Revisions,
    decimal AverageIncreasePercent);

public sealed record CycleBody(string Name, DateOnly EffectiveDate, decimal BudgetPercent, string? Notes);

public sealed record RevisionItem(
    Guid Id,
    Guid EmployeeId,
    string? EmployeeName,
    string? EmployeeCode,
    Guid? CycleId,
    string? CycleName,
    RevisionType Type,
    decimal CurrentCtc,
    decimal ProposedCtc,
    decimal IncreasePercent,
    string Currency,
    DateOnly EffectiveDate,
    Guid? NewGradeId,
    string? NewGradeName,
    string? NewDesignation,
    string Justification,
    string? PerformanceRating,
    RevisionStatus Status,
    string? ProposedByName,
    DateTime? DecidedDate,
    DateTime? AppliedDate,
    string? ApplyError,
    decimal? CompaRatioBefore,
    decimal? CompaRatioAfter);

public sealed record RevisionBody(Guid EmployeeId, RevisionDetails Details, bool Submit);

public sealed record RevisionQuery
{
    public Guid? CycleId { get; init; }

    public RevisionStatus? Status { get; init; }

    public Guid? EmployeeId { get; init; }
}

public sealed record RescheduleRevisionBody(DateOnly EffectiveDate);

public sealed record AwardItem(
    Guid Id,
    Guid EmployeeId,
    string? EmployeeName,
    string? EmployeeCode,
    AwardType Type,
    decimal Amount,
    string Currency,
    DateOnly PayoutMonth,
    string Reason,
    Guid? ProjectId,
    AwardStatus Status,
    string? ProposedByName,
    DateTime? DecidedDate);

public sealed record AwardBody(Guid EmployeeId, AwardType Type, decimal Amount, string? Currency, DateOnly PayoutMonth, string Reason, Guid? ProjectId);

public sealed record GrantItem(
    Guid Id,
    Guid EmployeeId,
    string? EmployeeName,
    string PlanName,
    DateOnly GrantDate,
    decimal Units,
    decimal StrikePrice,
    decimal? FairValuePerUnit,
    string Currency,
    DateOnly VestingStart,
    int VestingMonths,
    int CliffMonths,
    int VestingFrequencyMonths,
    decimal VestedUnits,
    decimal ExercisedUnits,
    decimal? VestedValue,
    GrantStatus Status,
    DateOnly? NextVestingDate,
    IReadOnlyList<VestingEvent> Schedule,
    string? Notes);

public sealed record GrantBody(Guid EmployeeId, GrantDetails Details);

public sealed record ExerciseBody(decimal Units);

public sealed record BandPosition(string? GradeName, decimal Minimum, decimal Midpoint, decimal Maximum, decimal CompaRatio, decimal Penetration, string Position);

/// <summary>Everything someone earns (spec §23): salary, variable pay, benefits and equity, with their band position.</summary>
public sealed record TotalRewards(
    Guid EmployeeId,
    string? EmployeeName,
    string Currency,
    decimal AnnualCtc,
    IReadOnlyList<SalaryLine> SalaryLines,
    BandPosition? Band,
    decimal VariablePayLast12Months,
    decimal BenefitsEmployerAnnual,
    IReadOnlyList<BenefitCharge> Benefits,
    decimal EquityVestedValue,
    decimal TotalAnnualRewards,
    IReadOnlyList<RevisionItem> Revisions,
    IReadOnlyList<AwardItem> Awards,
    IReadOnlyList<GrantItem> Grants);

/// <summary>
/// Compensation (spec §23): salary bands and compa-ratio, review cycles with a budget, revisions that
/// are approved and then applied to payroll, variable pay paid through payroll, and equity grants.
/// </summary>
internal sealed class CompensationService(
    AppDbContext dbContext,
    IEmployeeDirectory employees,
    IOrgStructure organization,
    IPayrollSalaries salaries,
    IPayrollInputs payrollInputs,
    IBenefitCharges benefits,
    IWorkflowStarter workflow,
    INotifier notifier,
    IPermissionProvider permissions,
    ICurrentUser currentUser,
    ITenantClock clock,
    TimeProvider timeProvider)
{
    public const string RevisionSubjectType = "compensation.revision";
    public const string AwardSubjectType = "compensation.variable-pay";
    public const string PayrollSource = "Compensation";

    public static WorkflowSubject RevisionSubject { get; } = new(
        RevisionSubjectType,
        "Approve a salary revision",
        "compensation",
        [
            new WorkflowSubjectField("increasePercent", "Increase (%)", WorkflowFieldType.Number),
            new WorkflowSubjectField("proposedCtc", "Proposed annual CTC", WorkflowFieldType.Number),
            new WorkflowSubjectField("type", "Revision type", WorkflowFieldType.Text),
        ]);

    public static WorkflowSubject AwardSubject { get; } = new(
        AwardSubjectType,
        "Approve variable pay",
        "compensation",
        [
            new WorkflowSubjectField("amount", "Amount", WorkflowFieldType.Number),
            new WorkflowSubjectField("type", "Award type", WorkflowFieldType.Text),
        ]);

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    // ----- Bands --------------------------------------------------------------------------------

    public async Task<IReadOnlyList<BandItem>> BandsAsync(CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        List<SalaryBand> bands = await dbContext.SalaryBands.AsNoTracking().OrderBy(band => band.GradeId).ThenByDescending(band => band.EffectiveFrom).ToListAsync(cancellationToken);
        IReadOnlyDictionary<Guid, string> grades = await organization.GetNamesAsync(OrgUnitKind.Grade, bands.Select(band => band.GradeId), cancellationToken);
        HashSet<Guid> current = [.. bands.Where(band => band.EffectiveFrom <= today).GroupBy(band => (band.GradeId, band.Currency)).Select(group => group.MaxBy(band => band.EffectiveFrom)!.Id)];

        // Who sits in each current band, and where.
        List<EmployeeSummary> people = [.. await employees.GetManyAsync(await employees.ListActiveIdsAsync(null, null, cancellationToken), cancellationToken)];
        IReadOnlyDictionary<Guid, SalaryInfo> pay = await salaries.CurrentAsync(people.Select(person => person.Id), today, cancellationToken);
        return [.. bands
            .Select(band =>
            {
                List<decimal> ctcs = !current.Contains(band.Id) ? [] : [.. people
                    .Where(person => person.GradeId == band.GradeId && pay.TryGetValue(person.Id, out SalaryInfo? info) && info.Currency == band.Currency)
                    .Select(person => pay[person.Id].AnnualCtc)];
                return new BandItem(
                    band.Id, band.GradeId, grades.GetValueOrDefault(band.GradeId), band.Currency, band.Minimum, band.Midpoint, band.Maximum, band.EffectiveFrom, band.Notes,
                    current.Contains(band.Id), ctcs.Count, ctcs.Count == 0 ? null : Math.Round(ctcs.Average(band.CompaRatio), 2),
                    ctcs.Count(ctc => ctc < band.Minimum), ctcs.Count(ctc => ctc > band.Maximum));
            })
            .OrderBy(item => item.GradeName, StringComparer.CurrentCulture)
            .ThenByDescending(item => item.EffectiveFrom)];
    }

    public async Task<BandItem> SaveBandAsync(Guid? id, BandBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        if ((await organization.FindInvalidAsync([new OrgReference(OrgUnitKind.Grade, body.GradeId)], cancellationToken)).Count > 0)
        {
            throw new DomainRuleException("BAND_GRADE", "Choose an active grade.");
        }

        string currency = string.IsNullOrWhiteSpace(body.Currency) ? "INR" : body.Currency;
        SalaryBand band;
        if (id is { } existing)
        {
            band = await dbContext.SalaryBands.FirstOrDefaultAsync(row => row.Id == existing, cancellationToken) ?? throw new NotFoundException(nameof(SalaryBand), existing);
            band.Update(currency, body.Minimum, body.Midpoint, body.Maximum, body.EffectiveFrom, body.Notes);
        }
        else
        {
            band = SalaryBand.Create(body.GradeId, currency, body.Minimum, body.Midpoint, body.Maximum, body.EffectiveFrom, body.Notes);
            dbContext.SalaryBands.Add(band);
        }

        if (await dbContext.SalaryBands.AnyAsync(row => row.Id != band.Id && row.GradeId == band.GradeId && row.Currency == band.Currency && row.EffectiveFrom == band.EffectiveFrom, cancellationToken))
        {
            throw new DomainRuleException("BAND_DATE", "This grade already has a band from that date.");
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return (await BandsAsync(cancellationToken)).First(item => item.Id == band.Id);
    }

    public async Task DeleteBandAsync(Guid id, CancellationToken cancellationToken)
    {
        SalaryBand band = await dbContext.SalaryBands.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(SalaryBand), id);
        dbContext.SalaryBands.Remove(band);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    // ----- Cycles -------------------------------------------------------------------------------

    public async Task<IReadOnlyList<CycleItem>> CyclesAsync(CancellationToken cancellationToken)
    {
        List<CompensationCycle> cycles = await dbContext.CompensationCycles.AsNoTracking().OrderByDescending(cycle => cycle.EffectiveDate).ToListAsync(cancellationToken);
        List<CompensationRevision> revisions = await dbContext.CompensationRevisions.AsNoTracking().Where(revision => revision.CycleId != null && revision.Status != RevisionStatus.Cancelled && revision.Status != RevisionStatus.Rejected).ToListAsync(cancellationToken);
        decimal payroll = (await salaries.CurrentAsync(await employees.ListActiveIdsAsync(null, null, cancellationToken), null, cancellationToken)).Values.Sum(info => info.AnnualCtc);
        return [.. cycles.Select(cycle =>
        {
            List<CompensationRevision> mine = [.. revisions.Where(revision => revision.CycleId == cycle.Id)];
            decimal committed = mine.Where(revision => revision.Status is RevisionStatus.Approved or RevisionStatus.Applied).Sum(revision => revision.ProposedCtc - revision.CurrentCtc);
            decimal proposed = mine.Sum(revision => revision.ProposedCtc - revision.CurrentCtc);
            return new CycleItem(
                cycle.Id, cycle.Name, cycle.EffectiveDate, cycle.BudgetPercent, cycle.Status, cycle.Notes, Math.Round(payroll * cycle.BudgetPercent / 100, 0), committed, proposed, mine.Count,
                mine.Count == 0 ? 0 : Math.Round(mine.Average(revision => revision.IncreasePercent), 2));
        })];
    }

    public async Task<CycleItem> SaveCycleAsync(Guid? id, CycleBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        CompensationCycle cycle;
        if (id is { } existing)
        {
            cycle = await dbContext.CompensationCycles.FirstOrDefaultAsync(row => row.Id == existing, cancellationToken) ?? throw new NotFoundException(nameof(CompensationCycle), existing);
            cycle.Update(body.Name, body.EffectiveDate, body.BudgetPercent, body.Notes);
        }
        else
        {
            cycle = CompensationCycle.Plan(body.Name, body.EffectiveDate, body.BudgetPercent, body.Notes);
            dbContext.CompensationCycles.Add(cycle);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return (await CyclesAsync(cancellationToken)).First(item => item.Id == cycle.Id);
    }

    public async Task<CycleItem> SetCycleStatusAsync(Guid id, CycleStatus status, CancellationToken cancellationToken)
    {
        CompensationCycle cycle = await dbContext.CompensationCycles.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(CompensationCycle), id);
        if (status == CycleStatus.Open)
        {
            cycle.Open();
        }
        else if (status == CycleStatus.Closed)
        {
            cycle.Close();
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return (await CyclesAsync(cancellationToken)).First(item => item.Id == cycle.Id);
    }

    // ----- Revisions ----------------------------------------------------------------------------

    public async Task<IReadOnlyList<RevisionItem>> RevisionsAsync(RevisionQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        IReadOnlyCollection<Guid>? scope = await employees.InScopeIdsAsync(Permissions.Compensation.RevisionView, cancellationToken);
        IQueryable<CompensationRevision> rows = dbContext.CompensationRevisions.AsNoTracking();
        if (scope is not null)
        {
            rows = rows.Where(row => scope.Contains(row.EmployeeId));
        }

        if (query.CycleId is { } cycle)
        {
            rows = rows.Where(row => row.CycleId == cycle);
        }

        if (query.Status is { } status)
        {
            rows = rows.Where(row => row.Status == status);
        }

        if (query.EmployeeId is { } employee)
        {
            rows = rows.Where(row => row.EmployeeId == employee);
        }

        return await RevisionItemsAsync(await rows.OrderByDescending(row => row.CreatedDate).Take(500).ToListAsync(cancellationToken), cancellationToken);
    }

    public async Task<RevisionItem> ProposeAsync(RevisionBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await EnsureReachAsync(body.EmployeeId, Permissions.Compensation.RevisionManage, cancellationToken);
        SalaryInfo current = (await salaries.CurrentAsync([body.EmployeeId], body.Details.EffectiveDate, cancellationToken)).GetValueOrDefault(body.EmployeeId)
            ?? (await salaries.CurrentAsync([body.EmployeeId], null, cancellationToken)).GetValueOrDefault(body.EmployeeId)
            ?? throw new DomainRuleException("REVISION_NO_SALARY", "This person has no salary in payroll yet; assign one there first.");
        if (body.Details.CycleId is { } cycleId && !await dbContext.CompensationCycles.AnyAsync(cycle => cycle.Id == cycleId && cycle.Status != CycleStatus.Closed, cancellationToken))
        {
            throw new DomainRuleException("REVISION_CYCLE", "That review cycle is closed.");
        }

        var revision = CompensationRevision.Propose(body.EmployeeId, current.AnnualCtc, current.Currency, currentUser.EmployeeId, body.Details);
        dbContext.CompensationRevisions.Add(revision);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (body.Submit)
        {
            await SubmitRevisionAsync(revision, cancellationToken);
        }

        return (await RevisionItemsAsync([revision], cancellationToken))[0];
    }

    public async Task<RevisionItem> UpdateRevisionAsync(Guid id, RevisionDetails details, CancellationToken cancellationToken)
    {
        CompensationRevision revision = await RevisionAsync(id, Permissions.Compensation.RevisionManage, cancellationToken);
        revision.Update(details);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (await RevisionItemsAsync([revision], cancellationToken))[0];
    }

    public async Task<RevisionItem> SubmitRevisionAsync(Guid id, CancellationToken cancellationToken)
    {
        CompensationRevision revision = await RevisionAsync(id, Permissions.Compensation.RevisionManage, cancellationToken);
        await SubmitRevisionAsync(revision, cancellationToken);
        return (await RevisionItemsAsync([revision], cancellationToken))[0];
    }

    public async Task<RevisionItem> CancelRevisionAsync(Guid id, CancellationToken cancellationToken)
    {
        CompensationRevision revision = await RevisionAsync(id, Permissions.Compensation.RevisionManage, cancellationToken);
        revision.Cancel();
        await workflow.WithdrawAsync(RevisionSubjectType, revision.Id, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (await RevisionItemsAsync([revision], cancellationToken))[0];
    }

    /// <summary>An approved revision payroll refused (its date falls in a finalized month): move it and apply again.</summary>
    public async Task<RevisionItem> RescheduleRevisionAsync(Guid id, DateOnly effectiveDate, CancellationToken cancellationToken)
    {
        CompensationRevision revision = await RevisionAsync(id, Permissions.Compensation.RevisionManage, cancellationToken);
        revision.Reschedule(effectiveDate);
        await ApplyAsync(revision, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (await RevisionItemsAsync([revision], cancellationToken))[0];
    }

    public async Task DecideRevisionAsync(Guid revisionId, bool? approved, CancellationToken cancellationToken)
    {
        CompensationRevision? revision = await dbContext.CompensationRevisions.FirstOrDefaultAsync(row => row.Id == revisionId, cancellationToken);
        if (revision?.Status != RevisionStatus.PendingApproval)
        {
            return;
        }

        if (approved is null)
        {
            revision.ReturnToDraft();
        }
        else
        {
            revision.Decide(approved.Value, Now);
            if (approved.Value)
            {
                await ApplyAsync(revision, cancellationToken);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    // ----- Variable pay -------------------------------------------------------------------------

    public async Task<IReadOnlyList<AwardItem>> AwardsAsync(AwardStatus? status, Guid? employeeId, CancellationToken cancellationToken)
    {
        IReadOnlyCollection<Guid>? scope = await employees.InScopeIdsAsync(Permissions.Compensation.VariablePayView, cancellationToken);
        IQueryable<VariablePayAward> rows = dbContext.VariablePayAwards.AsNoTracking();
        if (scope is not null)
        {
            rows = rows.Where(row => scope.Contains(row.EmployeeId));
        }

        if (status is { } wanted)
        {
            rows = rows.Where(row => row.Status == wanted);
        }

        if (employeeId is { } person)
        {
            rows = rows.Where(row => row.EmployeeId == person);
        }

        return await AwardItemsAsync(await rows.OrderByDescending(row => row.PayoutMonth).ThenByDescending(row => row.CreatedDate).Take(500).ToListAsync(cancellationToken), cancellationToken);
    }

    public async Task<AwardItem> ProposeAwardAsync(AwardBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await EnsureReachAsync(body.EmployeeId, Permissions.Compensation.VariablePayManage, cancellationToken);
        var award = VariablePayAward.Propose(body.EmployeeId, body.Type, body.Amount, body.Currency ?? "INR", body.PayoutMonth, body.Reason, body.ProjectId, currentUser.EmployeeId);
        dbContext.VariablePayAwards.Add(award);
        await dbContext.SaveChangesAsync(cancellationToken);

        WorkflowStartResult started = await workflow.StartAsync(
            new WorkflowStartRequest
            {
                SubjectType = AwardSubjectType,
                SubjectId = award.Id,
                Title = $"{Humanize(award.Type)} of {award.Amount.ToString("N0", CultureInfo.InvariantCulture)} {award.Currency}",
                RequesterEmployeeId = currentUser.EmployeeId,
                Link = "/compensation/variable-pay",
                Fields = new Dictionary<string, string>
                {
                    ["amount"] = award.Amount.ToString(CultureInfo.InvariantCulture),
                    ["type"] = award.Type.ToString(),
                },
            },
            cancellationToken);
        if (started.Outcome == WorkflowStartOutcome.Pending)
        {
            award.AwaitApproval(started.InstanceId!.Value);
        }
        else
        {
            award.Decide(approved: true, Now);
            await SendToPayrollAsync(award, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return (await AwardItemsAsync([award], cancellationToken))[0];
    }

    public async Task<AwardItem> CancelAwardAsync(Guid id, CancellationToken cancellationToken)
    {
        VariablePayAward award = await dbContext.VariablePayAwards.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(VariablePayAward), id);
        await EnsureReachAsync(award.EmployeeId, Permissions.Compensation.VariablePayManage, cancellationToken);
        if (award.Status == AwardStatus.Approved && !await payrollInputs.WithdrawAsync(PayrollSource, award.Id, cancellationToken))
        {
            throw new DomainRuleException("AWARD_PAID", "Payroll has already paid this award.");
        }

        award.Cancel();
        await workflow.WithdrawAsync(AwardSubjectType, award.Id, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (await AwardItemsAsync([award], cancellationToken))[0];
    }

    public async Task DecideAwardAsync(Guid awardId, bool approved, CancellationToken cancellationToken)
    {
        VariablePayAward? award = await dbContext.VariablePayAwards.FirstOrDefaultAsync(row => row.Id == awardId, cancellationToken);
        if (award?.Status != AwardStatus.PendingApproval)
        {
            return;
        }

        award.Decide(approved, Now);
        if (approved)
        {
            await SendToPayrollAsync(award, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Payroll paid these awards in a finalized run.</summary>
    public async Task MarkPaidAsync(IEnumerable<Guid> awardIds, CancellationToken cancellationToken)
    {
        List<Guid> ids = [.. awardIds];
        foreach (VariablePayAward award in await dbContext.VariablePayAwards.Where(row => ids.Contains(row.Id) && row.Status == AwardStatus.Approved).ToListAsync(cancellationToken))
        {
            award.MarkPaid();
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    // ----- Equity -------------------------------------------------------------------------------

    public async Task<IReadOnlyList<GrantItem>> GrantsAsync(Guid? employeeId, CancellationToken cancellationToken)
    {
        IReadOnlyCollection<Guid>? scope = await employees.InScopeIdsAsync(Permissions.Compensation.EquityView, cancellationToken);
        IQueryable<EquityGrant> rows = dbContext.EquityGrants.AsNoTracking();
        if (scope is not null)
        {
            rows = rows.Where(row => scope.Contains(row.EmployeeId));
        }

        if (employeeId is { } person)
        {
            rows = rows.Where(row => row.EmployeeId == person);
        }

        return await GrantItemsAsync(await rows.OrderByDescending(row => row.GrantDate).ToListAsync(cancellationToken), cancellationToken);
    }

    public async Task<GrantItem> SaveGrantAsync(Guid? id, GrantBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await EnsureReachAsync(body.EmployeeId, Permissions.Compensation.EquityManage, cancellationToken);
        EquityGrant grant;
        if (id is { } existing)
        {
            grant = await dbContext.EquityGrants.FirstOrDefaultAsync(row => row.Id == existing, cancellationToken) ?? throw new NotFoundException(nameof(EquityGrant), existing);
            grant.Update(body.Details);
        }
        else
        {
            grant = EquityGrant.Create(body.EmployeeId, body.Details);
            dbContext.EquityGrants.Add(grant);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return (await GrantItemsAsync([grant], cancellationToken))[0];
    }

    public async Task<GrantItem> ExerciseAsync(Guid id, decimal units, CancellationToken cancellationToken)
    {
        EquityGrant grant = await GrantAsync(id, cancellationToken);
        grant.Exercise(units, await clock.TodayAsync(cancellationToken));
        await dbContext.SaveChangesAsync(cancellationToken);
        return (await GrantItemsAsync([grant], cancellationToken))[0];
    }

    public async Task<GrantItem> CancelGrantAsync(Guid id, CancellationToken cancellationToken)
    {
        EquityGrant grant = await GrantAsync(id, cancellationToken);
        grant.Cancel(await clock.TodayAsync(cancellationToken));
        await dbContext.SaveChangesAsync(cancellationToken);
        return (await GrantItemsAsync([grant], cancellationToken))[0];
    }

    // ----- Total rewards ------------------------------------------------------------------------

    public async Task<TotalRewards> EmployeeRewardsAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        PermissionSet granted = await permissions.GetCurrentAsync(cancellationToken);
        if (!granted.Has(Permissions.Compensation.RevisionView) || !await employees.IsInScopeAsync(employeeId, Permissions.Compensation.RevisionView, cancellationToken))
        {
            throw new NotFoundException("Employee", employeeId);
        }

        return await RewardsAsync(employeeId, cancellationToken);
    }

    public Task<TotalRewards> MyRewardsAsync(CancellationToken cancellationToken)
        => RewardsAsync(currentUser.EmployeeId ?? throw new DomainRuleException("COMPENSATION_NO_EMPLOYEE", "Your account is not linked to an employee record."), cancellationToken);

    private async Task<TotalRewards> RewardsAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        EmployeeSummary? person = await employees.GetAsync(employeeId, cancellationToken);
        SalaryInfo? salary = (await salaries.CurrentAsync([employeeId], today, cancellationToken)).GetValueOrDefault(employeeId);
        string currency = salary?.Currency ?? "INR";

        BandPosition? band = null;
        if (person?.GradeId is { } grade && salary is not null)
        {
            SalaryBand? current = await dbContext.SalaryBands.AsNoTracking()
                .Where(row => row.GradeId == grade && row.Currency == currency && row.EffectiveFrom <= today)
                .OrderByDescending(row => row.EffectiveFrom)
                .FirstOrDefaultAsync(cancellationToken);
            if (current is not null)
            {
                string? gradeName = (await organization.GetNamesAsync(OrgUnitKind.Grade, [grade], cancellationToken)).GetValueOrDefault(grade);
                band = new BandPosition(gradeName, current.Minimum, current.Midpoint, current.Maximum, current.CompaRatio(salary.AnnualCtc), current.Penetration(salary.AnnualCtc), current.Position(salary.AnnualCtc));
            }
        }

        var month = new DateOnly(today.Year, today.Month, 1);
        IReadOnlyList<BenefitCharge> charges = await benefits.ForPeriodAsync([employeeId], month, month.AddMonths(1).AddDays(-1), cancellationToken);
        List<RevisionItem> revisions = [.. (await RevisionItemsAsync(await dbContext.CompensationRevisions.AsNoTracking().Where(row => row.EmployeeId == employeeId && row.Status == RevisionStatus.Applied).OrderByDescending(row => row.EffectiveDate).ToListAsync(cancellationToken), cancellationToken))];
        List<AwardItem> awards = [.. await AwardItemsAsync(await dbContext.VariablePayAwards.AsNoTracking().Where(row => row.EmployeeId == employeeId && row.Status != AwardStatus.Cancelled && row.Status != AwardStatus.Rejected).OrderByDescending(row => row.PayoutMonth).ToListAsync(cancellationToken), cancellationToken)];
        List<GrantItem> grants = [.. await GrantItemsAsync(await dbContext.EquityGrants.AsNoTracking().Where(row => row.EmployeeId == employeeId).ToListAsync(cancellationToken), cancellationToken)];

        decimal variable = awards.Where(award => award.Status is AwardStatus.Approved or AwardStatus.Paid && award.PayoutMonth > month.AddMonths(-12) && award.PayoutMonth <= month).Sum(award => award.Amount);
        decimal benefitsAnnual = charges.Sum(charge => charge.EmployerAmount) * 12;
        decimal equity = grants.Sum(grant => grant.VestedValue ?? 0);
        decimal ctc = salary?.AnnualCtc ?? 0;
        return new TotalRewards(
            employeeId, person?.DisplayName, currency, ctc, salary?.Lines ?? [], band, variable, benefitsAnnual, charges, equity, ctc + variable + benefitsAnnual,
            revisions, awards, grants);
    }

    // ----- Internals ----------------------------------------------------------------------------

    private async Task SubmitRevisionAsync(CompensationRevision revision, CancellationToken cancellationToken)
    {
        revision.Submit();
        await dbContext.SaveChangesAsync(cancellationToken);
        WorkflowStartResult started = await workflow.StartAsync(
            new WorkflowStartRequest
            {
                SubjectType = RevisionSubjectType,
                SubjectId = revision.Id,
                Title = $"{Humanize(revision.Type)} revision: {revision.CurrentCtc.ToString("N0", CultureInfo.InvariantCulture)} → {revision.ProposedCtc.ToString("N0", CultureInfo.InvariantCulture)} {revision.Currency} ({revision.IncreasePercent:0.#}%)",
                RequesterEmployeeId = currentUser.EmployeeId,
                Link = "/compensation/revisions",
                Fields = new Dictionary<string, string>
                {
                    ["increasePercent"] = revision.IncreasePercent.ToString(CultureInfo.InvariantCulture),
                    ["proposedCtc"] = revision.ProposedCtc.ToString(CultureInfo.InvariantCulture),
                    ["type"] = revision.Type.ToString(),
                },
            },
            cancellationToken);
        if (started.Outcome == WorkflowStartOutcome.Pending)
        {
            revision.AwaitApproval(started.InstanceId!.Value);
        }
        else
        {
            revision.Decide(approved: true, Now);
            await ApplyAsync(revision, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Payroll records the new CTC. A refusal (a date in a finalized month) is kept on the revision to fix and retry.</summary>
    private async Task ApplyAsync(CompensationRevision revision, CancellationToken cancellationToken)
    {
        try
        {
            Guid salaryId = await salaries.ReviseAsync(
                new SalaryRevisionRequest(
                    revision.EmployeeId, revision.ProposedCtc, revision.EffectiveDate, revision.NewStructureId,
                    revision.Type == RevisionType.Promotion ? "Promotion" : revision.Type == RevisionType.Correction ? "Correction" : "Revision", PayrollSource, revision.Id),
                cancellationToken);
            revision.Applied(salaryId, Now);
            await notifier.NotifyAsync(
                new NotificationRequest(
                    NotificationTemplates.CompensationRevisionDecided,
                    [new NotificationRecipient(revision.EmployeeId)],
                    new Dictionary<string, string>
                    {
                        ["revisionType"] = Humanize(revision.Type).ToLowerInvariant(),
                        ["effectiveDate"] = revision.EffectiveDate.ToString("d MMM yyyy", CultureInfo.InvariantCulture),
                        ["decision"] = "approved",
                    },
                    "/me/compensation"),
                cancellationToken);
        }
        catch (DomainRuleException exception)
        {
            revision.ApplyFailed(exception.Message);
        }
    }

    private async Task SendToPayrollAsync(VariablePayAward award, CancellationToken cancellationToken)
    {
        PayrollInputKind kind = award.Type switch
        {
            AwardType.Commission => PayrollInputKind.Commission,
            AwardType.Incentive or AwardType.ProjectIncentive => PayrollInputKind.Incentive,
            _ => PayrollInputKind.Bonus,
        };
        Guid inputId = await payrollInputs.AddAsync(
            new PayrollInputRequest(award.EmployeeId, award.PayoutMonth, kind, award.Amount, $"{Humanize(award.Type)}: {award.Reason}", PayrollSource, award.Id),
            cancellationToken);
        award.SentToPayroll(inputId);
    }

    private async Task EnsureReachAsync(Guid employeeId, string permission, CancellationToken cancellationToken)
    {
        if (!await employees.IsInScopeAsync(employeeId, permission, cancellationToken))
        {
            throw new NotFoundException("Employee", employeeId);
        }
    }

    private async Task<CompensationRevision> RevisionAsync(Guid id, string permission, CancellationToken cancellationToken)
    {
        CompensationRevision revision = await dbContext.CompensationRevisions.FirstOrDefaultAsync(row => row.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(CompensationRevision), id);
        await EnsureReachAsync(revision.EmployeeId, permission, cancellationToken);
        return revision;
    }

    private async Task<EquityGrant> GrantAsync(Guid id, CancellationToken cancellationToken)
    {
        EquityGrant grant = await dbContext.EquityGrants.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(EquityGrant), id);
        await EnsureReachAsync(grant.EmployeeId, Permissions.Compensation.EquityManage, cancellationToken);
        return grant;
    }

    private async Task<List<RevisionItem>> RevisionItemsAsync(IReadOnlyList<CompensationRevision> revisions, CancellationToken cancellationToken)
    {
        IEnumerable<Guid> people = revisions.Select(row => row.EmployeeId).Concat(revisions.Select(row => row.ProposedByEmployeeId).OfType<Guid>());
        Dictionary<Guid, EmployeeSummary> names = (await employees.GetManyAsync(people, cancellationToken)).ToDictionary(person => person.Id);
        List<Guid> cycleIds = [.. revisions.Select(row => row.CycleId).OfType<Guid>().Distinct()];
        Dictionary<Guid, string> cycles = await dbContext.CompensationCycles.AsNoTracking().Where(cycle => cycleIds.Contains(cycle.Id)).ToDictionaryAsync(cycle => cycle.Id, cycle => cycle.Name, cancellationToken);
        IReadOnlyDictionary<Guid, string> grades = await organization.GetNamesAsync(OrgUnitKind.Grade, revisions.Select(row => row.NewGradeId).OfType<Guid>(), cancellationToken);

        // Compa-ratio before and after, against the band of the grade they are (or will be) in.
        List<SalaryBand> bands = await dbContext.SalaryBands.AsNoTracking().ToListAsync(cancellationToken);
        SalaryBand? BandFor(Guid? grade, string currency, DateOnly day) => grade is null ? null
            : bands.Where(band => band.GradeId == grade && band.Currency == currency && band.EffectiveFrom <= day).MaxBy(band => band.EffectiveFrom);

        return [.. revisions.Select(row =>
        {
            EmployeeSummary? person = names.GetValueOrDefault(row.EmployeeId);
            SalaryBand? before = BandFor(person?.GradeId, row.Currency, row.EffectiveDate);
            SalaryBand? after = BandFor(row.NewGradeId ?? person?.GradeId, row.Currency, row.EffectiveDate);
            return new RevisionItem(
                row.Id, row.EmployeeId, person?.DisplayName, person?.EmployeeCode, row.CycleId, row.CycleId is { } cycle ? cycles.GetValueOrDefault(cycle) : null, row.Type,
                row.CurrentCtc, row.ProposedCtc, row.IncreasePercent, row.Currency, row.EffectiveDate, row.NewGradeId,
                row.NewGradeId is { } grade ? grades.GetValueOrDefault(grade) : null, row.NewDesignation, row.Justification, row.PerformanceRating, row.Status,
                row.ProposedByEmployeeId is { } proposer ? names.GetValueOrDefault(proposer)?.DisplayName : null, row.DecidedDate, row.AppliedDate, row.ApplyError,
                before?.CompaRatio(row.CurrentCtc), after?.CompaRatio(row.ProposedCtc));
        })];
    }

    private async Task<List<AwardItem>> AwardItemsAsync(IReadOnlyList<VariablePayAward> awards, CancellationToken cancellationToken)
    {
        IEnumerable<Guid> people = awards.Select(row => row.EmployeeId).Concat(awards.Select(row => row.ProposedByEmployeeId).OfType<Guid>());
        Dictionary<Guid, EmployeeSummary> names = (await employees.GetManyAsync(people, cancellationToken)).ToDictionary(person => person.Id);
        return [.. awards.Select(row => new AwardItem(
            row.Id, row.EmployeeId, names.GetValueOrDefault(row.EmployeeId)?.DisplayName, names.GetValueOrDefault(row.EmployeeId)?.EmployeeCode, row.Type, row.Amount, row.Currency,
            row.PayoutMonth, row.Reason, row.ProjectId, row.Status, row.ProposedByEmployeeId is { } proposer ? names.GetValueOrDefault(proposer)?.DisplayName : null, row.DecidedDate))];
    }

    private async Task<List<GrantItem>> GrantItemsAsync(IReadOnlyList<EquityGrant> grants, CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        Dictionary<Guid, string> names = (await employees.GetManyAsync(grants.Select(row => row.EmployeeId), cancellationToken)).ToDictionary(person => person.Id, person => person.DisplayName);
        return [.. grants.Select(row =>
        {
            IReadOnlyList<VestingEvent> schedule = row.Schedule();
            decimal vested = row.VestedOn(today);
            return new GrantItem(
                row.Id, row.EmployeeId, names.GetValueOrDefault(row.EmployeeId), row.PlanName, row.GrantDate, row.Units, row.StrikePrice, row.FairValuePerUnit, row.Currency,
                row.VestingStart, row.VestingMonths, row.CliffMonths, row.VestingFrequencyMonths, vested, row.ExercisedUnits,
                row.FairValuePerUnit is { } value ? Math.Round(Math.Max(0, value - row.StrikePrice) * (vested - row.ExercisedUnits), 2) : null,
                row.Status, row.Status == GrantStatus.Active ? schedule.FirstOrDefault(vest => vest.Date > today)?.Date : null, schedule, row.Notes);
        })];
    }

    private static string Humanize(Enum value) => System.Text.RegularExpressions.Regex.Replace(value.ToString(), "(?<=[a-z])(?=[A-Z])", " ");
}
