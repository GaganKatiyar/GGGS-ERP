using System.Globalization;
using System.Text.Json;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Attendance.Contracts;
using Gggs.Modules.Benefits.Contracts;
using Gggs.Modules.Compliance.Contracts;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Leave.Contracts;
using Gggs.Modules.Notification.Contracts;
using Gggs.Modules.Organization.Contracts;
using Gggs.Modules.Payroll.Contracts;
using Gggs.Modules.Payroll.Domain;
using Gggs.Modules.Payroll.Infrastructure;
using Gggs.Modules.Workflow.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Payroll.Application;

public sealed record RunItem(
    Guid Id,
    DateOnly Period,
    DateOnly PeriodEnd,
    string Label,
    DateOnly PayDate,
    Guid? LegalEntityId,
    RunStatus Status,
    string Currency,
    int EmployeeCount,
    decimal Gross,
    decimal Deductions,
    decimal Net,
    decimal EmployerCost,
    int Warnings,
    int Errors,
    DateTime? CalculatedDate,
    DateTime? ApprovedDate,
    DateTime? FinalizedDate,
    DateTime? PaidDate,
    string? PaymentReference,
    string? Notes,
    Guid? WorkflowInstanceId,
    PayrollRunKind Kind = PayrollRunKind.Regular,
    Guid? EmployeeId = null,
    string? EmployeeName = null);

public sealed record ReadinessCheck(string Code, string Level, string Message, int Count);

public sealed record StatutoryTotals(decimal PfEmployee, decimal PfEmployer, decimal EsiEmployee, decimal EsiEmployer, decimal ProfessionalTax, decimal Tds, decimal LabourWelfare);

public sealed record RunDetail(RunItem Run, IReadOnlyList<ReadinessCheck> Readiness, StatutoryTotals Statutory, RunItem? Previous, decimal? NetVariancePercent, int Held);

public sealed record RunItemRow(
    Guid Id,
    Guid EmployeeId,
    string EmployeeCode,
    string EmployeeName,
    string? Designation,
    ItemStatus Status,
    decimal PaidDays,
    decimal LopDays,
    decimal Gross,
    decimal Deductions,
    decimal Net,
    bool OnHold,
    string? HoldReason,
    int IssueCount,
    string? FirstIssue);

public sealed record RunItemDetail(
    Guid Id,
    Guid RunId,
    Guid EmployeeId,
    string EmployeeCode,
    string EmployeeName,
    string? Designation,
    ItemStatus Status,
    decimal PeriodDays,
    decimal PaidDays,
    decimal LopDays,
    decimal AnnualCtc,
    decimal Gross,
    decimal Reimbursements,
    decimal Deductions,
    decimal Net,
    decimal EmployerContributions,
    IReadOnlyList<PayLine> Lines,
    IReadOnlyList<AppliedRule> Rules,
    TaxComputation? Tax,
    ProvidentFundFigures? ProvidentFund,
    IReadOnlyList<string> Issues,
    string? StateCode,
    bool OnHold,
    string? HoldReason);

public sealed record RunItemQuery
{
    public ItemStatus? Status { get; init; }

    public bool? OnHold { get; init; }

    public string? Search { get; init; }

    public int? Page { get; init; }

    public int? PageSize { get; init; }
}

public sealed record CreateRunBody(DateOnly Period, DateOnly? PayDate, Guid? LegalEntityId, string? Notes);

public sealed record HoldBody(string Reason);

public sealed record PaidBody(DateOnly PaidOn, string? Reference);

public sealed record SubmitRunOutcome(RunStatus Status, Guid? WorkflowInstanceId);

/// <summary>
/// Monthly payroll runs (spec §21): readiness, calculation, holds, approval through the workflow,
/// finalization - which publishes payslips and settles inputs - and payment.
/// </summary>
internal sealed class RunService(
    AppDbContext dbContext,
    PayrollCatalog catalog,
    InputService inputs,
    IEmployeeDirectory employees,
    IEmployeePayrollDetails payrollDetails,
    IOrgStructure organization,
    ILeaveCalendar leave,
    IAttendanceLedger attendance,
    IBenefitCharges benefits,
    IWorkflowStarter workflow,
    INotifier notifier,
    IIntegrationEventPublisher publisher,
    ICurrentUser currentUser,
    TimeProvider timeProvider)
{
    public const string SubjectType = "payroll.run";

    public static WorkflowSubject Subject { get; } = new(
        SubjectType,
        "Approve a payroll run",
        "payroll",
        [
            new WorkflowSubjectField("netPay", "Net pay", WorkflowFieldType.Number),
            new WorkflowSubjectField("employees", "Employees paid", WorkflowFieldType.Number),
            new WorkflowSubjectField("variancePercent", "Change in net pay from last month (%)", WorkflowFieldType.Number),
        ]);

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    // ----- Runs ---------------------------------------------------------------------------------

    public async Task<IReadOnlyList<RunItem>> ListAsync(int? year, CancellationToken cancellationToken)
    {
        IQueryable<PayrollRun> runs = dbContext.PayrollRuns.AsNoTracking();
        if (year is { } fy)
        {
            DateOnly start = FinancialYear.Start(fy);
            DateOnly end = FinancialYear.End(fy);
            runs = runs.Where(run => run.Period >= start && run.Period <= end);
        }

        List<PayrollRun> rows = await runs.OrderByDescending(run => run.Period).ThenByDescending(run => run.CreatedDate).Take(60).ToListAsync(cancellationToken);
        Dictionary<Guid, string> leavers = (await employees.GetManyAsync(rows.Where(run => run.EmployeeId != null).Select(run => run.EmployeeId!.Value).Distinct(), cancellationToken))
            .ToDictionary(person => person.Id, person => person.DisplayName);
        return [.. rows.Select(run => Item(run) with { EmployeeName = run.EmployeeId is { } leaver ? leavers.GetValueOrDefault(leaver) : null })];
    }

    public async Task<RunDetail> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        PayrollRun run = await RunAsync(id, tracked: false, cancellationToken);
        List<PayrollRunItem> items = await dbContext.PayrollRunItems.AsNoTracking().Where(item => item.RunId == id).ToListAsync(cancellationToken);
        PayrollRun? previous = await dbContext.PayrollRuns.AsNoTracking()
            .Where(other => other.Kind == PayrollRunKind.Regular && other.Period < run.Period && other.LegalEntityId == run.LegalEntityId && (other.Status == RunStatus.Finalized || other.Status == RunStatus.Paid))
            .OrderByDescending(other => other.Period)
            .FirstOrDefaultAsync(cancellationToken);

        IReadOnlyList<ReadinessCheck> readiness = run.IsLocked || run.Status == RunStatus.Cancelled ? [] : await ReadinessAsync(run, cancellationToken);
        List<PayrollRunItem> payable = [.. items.Where(item => item.IsPayable)];
        decimal Total(params string[] codes) => payable.SelectMany(item => item.Lines).Where(line => codes.Contains(line.Code)).Sum(line => line.Amount);
        var statutory = new StatutoryTotals(
            Total(StatutoryKeys.ProvidentFundEmployee), Total(StatutoryKeys.ProvidentFundEmployer), Total(StatutoryKeys.StateInsuranceEmployee),
            Total(StatutoryKeys.StateInsuranceEmployer), Total(StatutoryKeys.ProfessionalTax), Total(StatutoryKeys.IncomeTax),
            Total(StatutoryKeys.LabourWelfareEmployee, StatutoryKeys.LabourWelfareEmployer));
        decimal? variance = previous is { Net: > 0 } && run.Net > 0 ? Math.Round((run.Net - previous.Net) * 100 / previous.Net, 1) : null;
        return new RunDetail(Item(run), readiness, statutory, previous is null ? null : Item(previous), variance, items.Count(item => item.OnHold));
    }

    public async Task<RunItem> CreateAsync(CreateRunBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        DateOnly period = new(body.Period.Year, body.Period.Month, 1);
        if (await dbContext.PayrollRuns.AnyAsync(run => run.Kind == PayrollRunKind.Regular && run.Period == period && run.LegalEntityId == body.LegalEntityId && run.Status != RunStatus.Cancelled, cancellationToken))
        {
            throw new DomainRuleException("RUN_EXISTS", $"There is already a payroll run for {period:MMMM yyyy}.");
        }

        PayrollSettings settings = await catalog.SettingsAsync(cancellationToken);
        DateOnly end = period.AddMonths(1).AddDays(-1);
        DateOnly payDate = body.PayDate ?? new DateOnly(end.Year, end.Month, Math.Min(settings.PayDay, end.Day));
        var run = PayrollRun.Open(period, payDate, body.LegalEntityId, settings.DefaultCurrency, body.Notes);
        dbContext.PayrollRuns.Add(run);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Item(run);
    }

    /// <summary>Calculates (or recalculates) everyone in the run. Holds survive a recalculation.</summary>
    public async Task<RunDetail> CalculateAsync(Guid id, CancellationToken cancellationToken)
    {
        PayrollRun run = await RunAsync(id, tracked: true, cancellationToken);
        run.EnsureCalculable();
        PayrollSettings settings = await catalog.SettingsAsync(cancellationToken);

        List<EmployeeSummary> people = await PeopleAsync(run, cancellationToken);
        List<Guid> ids = [.. people.Select(person => person.Id)];

        if (settings.OvertimeFromTimesheets)
        {
            await inputs.ImportOvertimeAsync(run.Period, ids, cancellationToken);
        }

        List<EmployeeSalary> salaries = run.SalaryExcluded ? [] : await dbContext.EmployeeSalaries.AsNoTracking()
            .Where(salary => ids.Contains(salary.EmployeeId) && salary.EffectiveFrom <= run.PeriodEnd && (salary.EffectiveTo == null || salary.EffectiveTo >= run.Period))
            .ToListAsync(cancellationToken);
        IReadOnlyDictionary<Guid, ResolvedStructure> structures = await catalog.StructuresAsync(salaries.Select(salary => salary.StructureId), cancellationToken);
        Dictionary<Guid, SalaryComponent> components = (await catalog.ComponentsAsync(cancellationToken)).ToDictionary(component => component.Id);
        IReadOnlyDictionary<Guid, PayrollIdentity> identities = await payrollDetails.GetAsync(ids, reveal: false, cancellationToken);
        IReadOnlyList<StatutoryRuleVersion> rulesInForce = await catalog.RulesInForceAsync(run.PeriodEnd, cancellationToken);

        Dictionary<Guid, decimal> lop = (await leave.GetApprovedLeaveAsync(ids, run.Period, run.PeriodEnd, cancellationToken))
            .Where(day => !day.IsPaid)
            .GroupBy(day => day.EmployeeId)
            .ToDictionary(group => group.Key, group => group.Sum(day => day.IsHalfDay ? 0.5m : 1m));
        if (settings.LopFromAttendance)
        {
            foreach ((Guid employeeId, decimal absent) in await attendance.AbsenceDaysAsync(ids, run.Period, run.PeriodEnd, cancellationToken))
            {
                lop[employeeId] = lop.GetValueOrDefault(employeeId) + absent;
            }
        }

        // A leaver's settlement also pays anything still waiting for them in later months (a claim, a bonus).
        bool settlement = run.Kind == PayrollRunKind.FullAndFinal;
        List<PayrollInput> monthInputs = await dbContext.PayrollInputs
            .Where(input => (input.Period == run.Period || settlement) && ids.Contains(input.EmployeeId)
                && (input.Status == InputStatus.Pending || (input.Status == InputStatus.Included && input.RunId == run.Id)))
            .ToListAsync(cancellationToken);
        ILookup<Guid, BenefitCharge> charges = (await benefits.ForPeriodAsync(ids, run.Period, run.PeriodEnd, cancellationToken)).ToLookup(charge => charge.EmployeeId);
        int fy = FinancialYear.Of(run.Period);
        Dictionary<Guid, TaxDeclaration> declarations = await dbContext.TaxDeclarations.AsNoTracking()
            .Where(declaration => ids.Contains(declaration.EmployeeId) && declaration.FinancialYear == fy)
            .ToDictionaryAsync(declaration => declaration.EmployeeId, cancellationToken);
        IReadOnlyDictionary<Guid, YearToDate> ytd = await YearToDateAsync(ids, run.Period, cancellationToken);

        List<PayrollRunItem> previous = await dbContext.PayrollRunItems.Where(item => item.RunId == run.Id).ToListAsync(cancellationToken);
        Dictionary<Guid, string?> holds = previous.Where(item => item.OnHold).ToDictionary(item => item.EmployeeId, item => item.HoldReason);
        dbContext.PayrollRunItems.RemoveRange(previous);

        var items = new List<PayrollRunItem>();
        foreach (EmployeeSummary person in people)
        {
            PayrollIdentity? identity = identities.GetValueOrDefault(person.Id);
            string? state = identity?.ProfessionalTaxStateCode ?? settings.DefaultStateCode;
            List<SalarySegment> segments = [.. salaries
                .Where(salary => salary.EmployeeId == person.Id && structures.ContainsKey(salary.StructureId))
                .Select(salary => new SalarySegment(salary.Id, salary.AnnualCtc, structures[salary.StructureId].Rules, salary.PfEnabled, salary.EffectiveFrom, salary.EffectiveTo))];
            List<InputLine> lines = [.. monthInputs
                .Where(input => input.EmployeeId == person.Id && components.ContainsKey(input.ComponentId))
                .Select(input => new InputLine(ComponentRule.From(components[input.ComponentId]), input.Amount, input.Description))];

            PayResult result = PayCalculator.Calculate(new PayContext(
                person.Id, person.EmployeeCode, person.DisplayName, person.DepartmentId, person.Designation, run.Period, run.PeriodEnd, settings.ProrationBasis,
                person.JoiningDate, person.ExitDate ?? run.LastWorkingDay, segments, lop.GetValueOrDefault(person.Id), lines, [.. charges[person.Id]],
                PayrollCatalog.RulesFor(rulesInForce, state), Age(identity?.DateOfBirth, run.PeriodEnd), state,
                declarations.TryGetValue(person.Id, out TaxDeclaration? declaration) ? DeclarationService.Facts(declaration) : null,
                settings.DefaultRegime, ytd.GetValueOrDefault(person.Id) ?? YearToDate.None,
                identity?.BankAccountNumber is not null, identity?.Pan is not null, identity?.Uan is not null, InputsOnly: settlement));

            var item = PayrollRunItem.For(run.Id, result);
            if (holds.TryGetValue(person.Id, out string? reason))
            {
                item.Hold(reason ?? "Held");
            }

            items.Add(item);
        }

        dbContext.PayrollRunItems.AddRange(items);
        foreach (PayrollInput input in monthInputs.Where(input => input.Status == InputStatus.Pending))
        {
            input.IncludeIn(run.Id);
        }

        (int count, decimal gross, decimal deductions, decimal net, decimal employer) = Totals(items);
        run.Calculated(count, gross, deductions, net, employer, items.Count(item => item.Status == ItemStatus.Warning), items.Count(item => item.Status == ItemStatus.Error && !item.OnHold), Now);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await GetAsync(run.Id, cancellationToken);
    }

    public async Task<PagedResult<RunItemRow>> ItemsAsync(Guid runId, RunItemQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        await RunAsync(runId, tracked: false, cancellationToken);
        var page = new PageRequest { Page = query.Page ?? 1, PageSize = query.PageSize ?? 50 };
        IReadOnlyCollection<Guid>? scope = await employees.InScopeIdsAsync(Permissions.Payroll.RunView, cancellationToken);
        IQueryable<PayrollRunItem> items = dbContext.PayrollRunItems.AsNoTracking().Where(item => item.RunId == runId);
        if (scope is not null)
        {
            items = items.Where(item => scope.Contains(item.EmployeeId));
        }

        if (query.Status is { } status)
        {
            items = items.Where(item => item.Status == status);
        }

        if (query.OnHold is { } held)
        {
            items = items.Where(item => item.OnHold == held);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            string term = query.Search.Trim();
            items = items.Where(item => item.EmployeeName.Contains(term) || item.EmployeeCode.Contains(term));
        }

        PagedResult<PayrollRunItem> result = await items.OrderByDescending(item => item.Status).ThenBy(item => item.EmployeeName).ToPagedResultAsync(page, item => item, cancellationToken);
        return PagedResult.From<RunItemRow>(
            [.. result.Items.Select(item =>
            {
                IReadOnlyList<string> issues = item.Issues;
                return new RunItemRow(
                    item.Id, item.EmployeeId, item.EmployeeCode, item.EmployeeName, item.Designation, item.Status, item.PaidDays, item.LopDays, item.Gross,
                    item.Deductions, item.Net, item.OnHold, item.HoldReason, issues.Count, issues.Count > 0 ? issues[0] : null);
            })],
            page,
            result.TotalCount);
    }

    public async Task<RunItemDetail> ItemAsync(Guid runId, Guid itemId, CancellationToken cancellationToken)
    {
        PayrollRunItem item = await dbContext.PayrollRunItems.AsNoTracking().FirstOrDefaultAsync(row => row.Id == itemId && row.RunId == runId, cancellationToken)
            ?? throw new NotFoundException(nameof(PayrollRunItem), itemId);
        return await employees.IsInScopeAsync(item.EmployeeId, Permissions.Payroll.RunView, cancellationToken) ? Detail(item) : throw new NotFoundException(nameof(PayrollRunItem), itemId);
    }

    public async Task<RunItemDetail> HoldAsync(Guid runId, Guid itemId, string? reason, CancellationToken cancellationToken)
    {
        PayrollRun run = await RunAsync(runId, tracked: true, cancellationToken);
        run.EnsureCalculable();
        PayrollRunItem item = await dbContext.PayrollRunItems.FirstOrDefaultAsync(row => row.Id == itemId && row.RunId == runId, cancellationToken)
            ?? throw new NotFoundException(nameof(PayrollRunItem), itemId);
        if (reason is null)
        {
            item.Release();
        }
        else
        {
            item.Hold(reason);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        List<PayrollRunItem> items = await dbContext.PayrollRunItems.AsNoTracking().Where(row => row.RunId == runId).ToListAsync(cancellationToken);
        (int count, decimal gross, decimal deductions, decimal net, decimal employer) = Totals(items);
        run.Totals(count, gross, deductions, net, employer, items.Count(row => row.Status == ItemStatus.Warning), items.Count(row => row.Status == ItemStatus.Error && !row.OnHold));
        await dbContext.SaveChangesAsync(cancellationToken);
        return Detail(item);
    }

    public async Task<SubmitRunOutcome> SubmitAsync(Guid id, CancellationToken cancellationToken)
    {
        PayrollRun run = await RunAsync(id, tracked: true, cancellationToken);
        run.Submit();
        PayrollRun? previous = await dbContext.PayrollRuns.AsNoTracking()
            .Where(other => other.Period < run.Period && (other.Status == RunStatus.Finalized || other.Status == RunStatus.Paid))
            .OrderByDescending(other => other.Period)
            .FirstOrDefaultAsync(cancellationToken);
        decimal variance = previous is { Net: > 0 } ? Math.Round((run.Net - previous.Net) * 100 / previous.Net, 1) : 0;

        await workflow.WithdrawAsync(SubjectType, run.Id, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        WorkflowStartResult started = await workflow.StartAsync(
            new WorkflowStartRequest
            {
                SubjectType = SubjectType,
                SubjectId = run.Id,
                Title = $"Payroll for {Label(run.Period)}: {run.EmployeeCount} people, net {run.Net.ToString("N0", CultureInfo.InvariantCulture)} {run.Currency}",
                RequesterEmployeeId = currentUser.EmployeeId,
                Link = $"/payroll/runs/{run.Id}",
                Fields = new Dictionary<string, string>
                {
                    ["netPay"] = run.Net.ToString(CultureInfo.InvariantCulture),
                    ["employees"] = run.EmployeeCount.ToString(CultureInfo.InvariantCulture),
                    ["variancePercent"] = variance.ToString(CultureInfo.InvariantCulture),
                },
            },
            cancellationToken);

        if (started.Outcome == WorkflowStartOutcome.Pending)
        {
            run.AwaitApproval(started.InstanceId!.Value);
        }
        else
        {
            run.Approve(Now);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new SubmitRunOutcome(run.Status, started.InstanceId);
    }

    /// <summary>The approval workflow's outcome: approved, or back for rework.</summary>
    public async Task ApplyDecisionAsync(Guid runId, bool approved, CancellationToken cancellationToken)
    {
        PayrollRun? run = await dbContext.PayrollRuns.FirstOrDefaultAsync(row => row.Id == runId, cancellationToken);
        if (run?.Status != RunStatus.PendingApproval)
        {
            return;
        }

        if (approved)
        {
            run.Approve(Now);
        }
        else
        {
            run.ReturnForRework();
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Finalizes an approved run: payslips are published to everyone paid, their inputs settled, held
    /// people's inputs move to next month, and the numbers are fixed for good.
    /// </summary>
    public async Task<RunDetail> FinalizeAsync(Guid id, CancellationToken cancellationToken)
    {
        PayrollRun run = await RunAsync(id, tracked: true, cancellationToken);
        run.Finalize(currentUser.EmployeeId, Now);

        List<PayrollRunItem> items = await dbContext.PayrollRunItems.AsNoTracking().Where(item => item.RunId == run.Id).ToListAsync(cancellationToken);
        List<PayrollRunItem> paid = [.. items.Where(item => item.IsPayable)];
        HashSet<Guid> paidIds = [.. paid.Select(item => item.EmployeeId)];

        List<PayrollInput> included = await dbContext.PayrollInputs.Where(input => input.RunId == run.Id && input.Status == InputStatus.Included).ToListAsync(cancellationToken);
        foreach (PayrollInput input in included)
        {
            if (paidIds.Contains(input.EmployeeId))
            {
                input.MarkPaid();
            }
            else
            {
                input.Defer(run.Period.AddMonths(1));
            }
        }

        PayrollSettings settings = await catalog.SettingsAsync(cancellationToken);
        IReadOnlyDictionary<Guid, PayrollIdentity> identities = await payrollDetails.GetAsync(paidIds, reveal: false, cancellationToken);
        Dictionary<Guid, EmployeeSummary> people = (await employees.GetManyAsync(paidIds, cancellationToken)).ToDictionary(person => person.Id);
        IReadOnlyDictionary<Guid, string> departments = await organization.GetNamesAsync(OrgUnitKind.Department, paid.Select(item => item.DepartmentId).OfType<Guid>(), cancellationToken);
        IReadOnlyDictionary<Guid, YearToDate> ytd = await YearToDateAsync(paidIds, run.Period, cancellationToken);
        Dictionary<Guid, (decimal Gross, decimal Deductions, decimal Net)> ytdTotals = await YearToDateTotalsAsync(paidIds, run.Period, cancellationToken);

        foreach (PayrollRunItem item in paid)
        {
            PayslipDocument document = PayslipDocuments.Build(
                run, item, settings, identities.GetValueOrDefault(item.EmployeeId), people.GetValueOrDefault(item.EmployeeId),
                item.DepartmentId is { } department ? departments.GetValueOrDefault(department) : null,
                ytd.GetValueOrDefault(item.EmployeeId) ?? YearToDate.None, ytdTotals.GetValueOrDefault(item.EmployeeId));
            dbContext.Payslips.Add(Payslip.Publish(run, item, JsonSerializer.Serialize(document, PayslipDocuments.Json), Now));
        }

        publisher.Publish(new PayrollRunFinalizedIntegrationEvent(
            run.Id, run.Period, paid.Count, run.Net,
            [.. included.Where(input => input.Status == InputStatus.Paid && input.SourceId is not null && input.Source is not (InputService.ManualSource or InputService.TimesheetSource or FinalSettlementPayroll.Source))
                .Select(input => new PaidInput(input.Source, input.SourceId!.Value, input.EmployeeId, input.Amount))],
            run.Kind,
            run.SourceId));
        await dbContext.SaveChangesAsync(cancellationToken);

        string period = Label(run.Period);
        foreach (PayrollRunItem item in paid)
        {
            await notifier.NotifyAsync(
                new NotificationRequest(
                    NotificationTemplates.PayslipPublished,
                    [new NotificationRecipient(item.EmployeeId)],
                    new Dictionary<string, string> { ["period"] = period, ["netPay"] = $"{item.Net.ToString("N0", CultureInfo.InvariantCulture)} {run.Currency}" },
                    "/me/payslips"),
                cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await GetAsync(run.Id, cancellationToken);
    }

    public async Task<RunItem> MarkPaidAsync(Guid id, PaidBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        PayrollRun run = await RunAsync(id, tracked: true, cancellationToken);
        run.MarkPaid(body.PaidOn.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), body.Reference);
        publisher.Publish(new PayrollRunPaidIntegrationEvent(run.Id, run.Kind, run.SourceId, body.PaidOn, run.PaymentReference));
        await dbContext.SaveChangesAsync(cancellationToken);
        return Item(run);
    }

    public async Task<RunItem> CancelAsync(Guid id, CancellationToken cancellationToken)
    {
        PayrollRun run = await RunAsync(id, tracked: true, cancellationToken);
        run.Cancel();
        await workflow.WithdrawAsync(SubjectType, run.Id, cancellationToken);
        foreach (PayrollInput input in await dbContext.PayrollInputs.Where(row => row.RunId == run.Id && row.Status == InputStatus.Included).ToListAsync(cancellationToken))
        {
            // A settlement's own lines go with its run; anything else waits for the next payroll.
            if (input.Source == FinalSettlementPayroll.Source)
            {
                input.Cancel();
            }
            else
            {
                input.Release();
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return Item(run);
    }

    // ----- Internals ----------------------------------------------------------------------------

    internal static string Label(DateOnly period) => period.ToString("MMMM yyyy", CultureInfo.InvariantCulture);

    private async Task<PayrollRun> RunAsync(Guid id, bool tracked, CancellationToken cancellationToken)
    {
        IQueryable<PayrollRun> runs = tracked ? dbContext.PayrollRuns : dbContext.PayrollRuns.AsNoTracking();
        return await runs.FirstOrDefaultAsync(run => run.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(PayrollRun), id);
    }

    /// <summary>
    /// Everyone with a salary during the month, employed during it, within the caller's reach and the
    /// run's legal entity - less anyone whose full and final run pays that month. A full and final run
    /// pays its leaver alone.
    /// </summary>
    private async Task<List<EmployeeSummary>> PeopleAsync(PayrollRun run, CancellationToken cancellationToken)
    {
        if (run.Kind == PayrollRunKind.FullAndFinal)
        {
            return [.. await employees.GetManyAsync([run.EmployeeId!.Value], cancellationToken)];
        }

        HashSet<Guid> settled = [.. await dbContext.PayrollRuns.AsNoTracking()
            .Where(other => other.Kind == PayrollRunKind.FullAndFinal && other.Period == run.Period && other.Status != RunStatus.Cancelled && other.EmployeeId != null)
            .Select(other => other.EmployeeId!.Value)
            .ToListAsync(cancellationToken)];
        List<Guid> withSalary = await dbContext.EmployeeSalaries.AsNoTracking()
            .Where(salary => salary.EffectiveFrom <= run.PeriodEnd && (salary.EffectiveTo == null || salary.EffectiveTo >= run.Period))
            .Select(salary => salary.EmployeeId)
            .Distinct()
            .ToListAsync(cancellationToken);
        IReadOnlyCollection<Guid>? scope = await employees.InScopeIdsAsync(Permissions.Payroll.RunProcess, cancellationToken);
        return [.. (await employees.GetManyAsync(withSalary.Where(id => (scope is null || scope.Contains(id)) && !settled.Contains(id)), cancellationToken))
            .Where(person => (person.JoiningDate is null || person.JoiningDate <= run.PeriodEnd) && (person.ExitDate is null || person.ExitDate >= run.Period))
            .Where(person => run.LegalEntityId is null || person.LegalEntityId == run.LegalEntityId)
            .OrderBy(person => person.DisplayName, StringComparer.CurrentCulture)];
    }

    private async Task<IReadOnlyList<ReadinessCheck>> ReadinessAsync(PayrollRun run, CancellationToken cancellationToken)
    {
        var checks = new List<ReadinessCheck>();
        IReadOnlyCollection<Guid>? scope = await employees.InScopeIdsAsync(Permissions.Payroll.RunProcess, cancellationToken);
        List<Guid> active = [.. (await employees.ListActiveIdsAsync(null, null, cancellationToken)).Where(id => scope is null || scope.Contains(id))];
        HashSet<Guid> withSalary = [.. await dbContext.EmployeeSalaries.AsNoTracking()
            .Where(salary => active.Contains(salary.EmployeeId) && salary.EffectiveFrom <= run.PeriodEnd && (salary.EffectiveTo == null || salary.EffectiveTo >= run.Period))
            .Select(salary => salary.EmployeeId)
            .ToListAsync(cancellationToken)];
        int without = active.Count(id => !withSalary.Contains(id));
        if (without > 0)
        {
            checks.Add(new ReadinessCheck("NO_SALARY", "Warning", $"{without} active employee(s) have no salary for this month and will not be paid.", without));
        }

        int pending = await dbContext.PayrollInputs.CountAsync(input => input.Period == run.Period && input.Status == InputStatus.Pending, cancellationToken);
        if (pending > 0 && run.Status == RunStatus.Calculated)
        {
            checks.Add(new ReadinessCheck("PENDING_INPUTS", "Warning", $"{pending} input(s) were added after the last calculation; recalculate to include them.", pending));
        }

        int open = await dbContext.PayrollRuns.CountAsync(other => other.Period < run.Period && other.Status != RunStatus.Cancelled && other.Status != RunStatus.Finalized && other.Status != RunStatus.Paid, cancellationToken);
        if (open > 0)
        {
            checks.Add(new ReadinessCheck("OPEN_PREVIOUS", "Warning", "An earlier month's payroll is not finalized; year-to-date tax will not include it.", open));
        }

        int fy = FinancialYear.Of(run.Period);
        int declarations = await dbContext.TaxDeclarations.CountAsync(declaration => declaration.FinancialYear == fy && declaration.Status == DeclarationStatus.Submitted, cancellationToken);
        if (declarations > 0)
        {
            checks.Add(new ReadinessCheck("DECLARATIONS", "Info", $"{declarations} tax declaration(s) are waiting for verification; TDS uses the declared amounts until then.", declarations));
        }

        if (run.Status == RunStatus.Draft)
        {
            checks.Add(new ReadinessCheck("NOT_CALCULATED", "Info", "Lock attendance, leave and timesheets for the month, enter variable inputs, then calculate.", 0));
        }

        return checks;
    }

    /// <summary>Year-to-date tax figures from this financial year's finalized payroll, before <paramref name="period"/>.</summary>
    private async Task<IReadOnlyDictionary<Guid, YearToDate>> YearToDateAsync(IEnumerable<Guid> employeeIds, DateOnly period, CancellationToken cancellationToken)
    {
        List<PayrollRunItem> items = await PaidItemsBeforeAsync(employeeIds, period, cancellationToken);
        return items.GroupBy(item => item.EmployeeId).ToDictionary(group => group.Key, group =>
        {
            List<PayLine> lines = [.. group.SelectMany(item => item.Lines)];
            decimal Line(string code) => lines.Where(line => line.Code == code).Sum(line => line.Amount);
            return new YearToDate(
                group.Sum(item => item.TaxableEarnings), Line(StatutoryKeys.IncomeTax), Line(StatutoryKeys.ProfessionalTax), Line(StatutoryKeys.ProvidentFundEmployee),
                Line("BASIC"), Line("HRA"), group.Count());
        });
    }

    private async Task<Dictionary<Guid, (decimal Gross, decimal Deductions, decimal Net)>> YearToDateTotalsAsync(IEnumerable<Guid> employeeIds, DateOnly period, CancellationToken cancellationToken)
        => (await PaidItemsBeforeAsync(employeeIds, period, cancellationToken))
            .GroupBy(item => item.EmployeeId)
            .ToDictionary(group => group.Key, group => (group.Sum(item => item.Gross), group.Sum(item => item.Deductions), group.Sum(item => item.Net)));

    private async Task<List<PayrollRunItem>> PaidItemsBeforeAsync(IEnumerable<Guid> employeeIds, DateOnly period, CancellationToken cancellationToken)
    {
        List<Guid> ids = [.. employeeIds.Distinct()];
        DateOnly start = FinancialYear.Start(FinancialYear.Of(period));
        return await dbContext.Payslips.AsNoTracking()
            .Where(payslip => ids.Contains(payslip.EmployeeId) && payslip.Period >= start && payslip.Period < period)
            .Join(dbContext.PayrollRunItems.AsNoTracking(), payslip => payslip.RunItemId, item => item.Id, (payslip, item) => item)
            .ToListAsync(cancellationToken);
    }

    private static (int Count, decimal Gross, decimal Deductions, decimal Net, decimal Employer) Totals(IEnumerable<PayrollRunItem> items)
    {
        List<PayrollRunItem> payable = [.. items.Where(item => item.IsPayable)];
        return (payable.Count, payable.Sum(item => item.Gross + item.Reimbursements), payable.Sum(item => item.Deductions), payable.Sum(item => item.Net),
            payable.Sum(item => item.Gross + item.Reimbursements + item.EmployerContributions));
    }

    private static int? Age(DateOnly? birth, DateOnly on)
        => birth is { } born ? on.Year - born.Year - (on < born.AddYears(on.Year - born.Year) ? 1 : 0) : null;

    internal static RunItem Item(PayrollRun run) => new(
        run.Id, run.Period, run.PeriodEnd, Label(run.Period), run.PayDate, run.LegalEntityId, run.Status, run.Currency, run.EmployeeCount, run.Gross,
        run.Deductions, run.Net, run.EmployerCost, run.Warnings, run.Errors, run.CalculatedDate, run.ApprovedDate, run.FinalizedDate, run.PaidDate,
        run.PaymentReference, run.Notes, run.WorkflowInstanceId, run.Kind, run.EmployeeId);

    private static RunItemDetail Detail(PayrollRunItem item) => new(
        item.Id, item.RunId, item.EmployeeId, item.EmployeeCode, item.EmployeeName, item.Designation, item.Status, item.PeriodDays, item.PaidDays, item.LopDays,
        item.AnnualCtc, item.Gross, item.Reimbursements, item.Deductions, item.Net, item.EmployerContributions, item.Lines, item.Rules, item.Tax,
        item.ProvidentFund, item.Issues, item.StateCode, item.OnHold, item.HoldReason);
}
