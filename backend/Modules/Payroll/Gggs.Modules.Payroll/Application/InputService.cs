using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Compliance.Contracts;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Payroll.Contracts;
using Gggs.Modules.Payroll.Domain;
using Gggs.Modules.Payroll.Infrastructure;
using Gggs.Modules.Timesheets.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Payroll.Application;

public sealed record InputItem(
    Guid Id,
    Guid EmployeeId,
    string? EmployeeName,
    string? EmployeeCode,
    DateOnly Period,
    Guid ComponentId,
    string ComponentCode,
    string ComponentName,
    PayrollInputKind Kind,
    decimal Amount,
    decimal? Quantity,
    string Description,
    string Source,
    InputStatus Status,
    Guid? RunId);

public sealed record InputBody(Guid EmployeeId, DateOnly Period, Guid ComponentId, decimal Amount, decimal? Quantity, string Description);

/// <summary>
/// One-off payroll inputs (spec §21, §94): entered by payroll, raised by other modules (a bonus
/// award), or priced from approved overtime in timesheets. Payroll stays the engine that pays them.
/// </summary>
internal sealed class InputService(
    AppDbContext dbContext,
    PayrollCatalog catalog,
    IEmployeeDirectory employees,
    ITimesheetLedger timesheets) : IPayrollInputs
{
    public const string ManualSource = "Manual";
    public const string TimesheetSource = "Timesheets";

    public async Task<IReadOnlyList<InputItem>> ListAsync(DateOnly period, Guid? employeeId, CancellationToken cancellationToken)
    {
        DateOnly month = new(period.Year, period.Month, 1);
        IReadOnlyCollection<Guid>? scope = await employees.InScopeIdsAsync(Permissions.Payroll.InputManage, cancellationToken);
        IQueryable<PayrollInput> inputs = dbContext.PayrollInputs.AsNoTracking().Where(input => input.Period == month && input.Status != InputStatus.Cancelled);
        if (scope is not null)
        {
            inputs = inputs.Where(input => scope.Contains(input.EmployeeId));
        }

        if (employeeId is { } person)
        {
            inputs = inputs.Where(input => input.EmployeeId == person);
        }

        return await ItemsAsync(await inputs.OrderBy(input => input.EmployeeId).ThenBy(input => input.Kind).ToListAsync(cancellationToken), cancellationToken);
    }

    public async Task<InputItem> AddManualAsync(InputBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!await employees.IsInScopeAsync(body.EmployeeId, Permissions.Payroll.InputManage, cancellationToken))
        {
            throw new NotFoundException("Employee", body.EmployeeId);
        }

        DateOnly month = new(body.Period.Year, body.Period.Month, 1);
        await EnsureOpenAsync(month, cancellationToken);
        SalaryComponent component = (await catalog.ComponentsAsync(cancellationToken)).FirstOrDefault(row => row.Id == body.ComponentId && row.IsActive)
            ?? throw new DomainRuleException("INPUT_COMPONENT", "Choose an active input component.");
        var input = PayrollInput.Raise(body.EmployeeId, month, component, component.InputKind ?? PayrollInputKind.Bonus, body.Amount, body.Quantity, body.Description, ManualSource, null);
        dbContext.PayrollInputs.Add(input);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (await ItemsAsync([input], cancellationToken))[0];
    }

    public async Task CancelAsync(Guid id, CancellationToken cancellationToken)
    {
        PayrollInput input = await dbContext.PayrollInputs.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(PayrollInput), id);
        if (!await employees.IsInScopeAsync(input.EmployeeId, Permissions.Payroll.InputManage, cancellationToken))
        {
            throw new NotFoundException(nameof(PayrollInput), id);
        }

        input.Cancel();
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Prices the month's approved overtime from timesheets (spec §94): hours × (fixed monthly gross ÷
    /// the month's standard hours) × the overtime multiplier. Re-running refreshes the amounts.
    /// Returns how many people have overtime.
    /// </summary>
    public async Task<int> ImportOvertimeAsync(DateOnly period, IReadOnlyCollection<Guid> employeeIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(employeeIds);
        DateOnly month = new(period.Year, period.Month, 1);
        DateOnly end = month.AddMonths(1).AddDays(-1);
        SalaryComponent? component = (await catalog.ComponentsAsync(cancellationToken)).FirstOrDefault(row => row.IsActive && row.InputKind == PayrollInputKind.Overtime);
        if (component is null || employeeIds.Count == 0)
        {
            return 0;
        }

        PayrollSettings settings = await catalog.SettingsAsync(cancellationToken);
        IReadOnlyDictionary<Guid, decimal> overtime = await timesheets.OvertimeAsync(employeeIds, month, end, cancellationToken);
        List<PayrollInput> existing = await dbContext.PayrollInputs
            .Where(input => input.Period == month && input.Source == TimesheetSource && (input.Status == InputStatus.Pending || input.Status == InputStatus.Included))
            .ToListAsync(cancellationToken);

        List<EmployeeSalary> salaries = await dbContext.EmployeeSalaries.AsNoTracking()
            .Where(salary => employeeIds.Contains(salary.EmployeeId) && salary.EffectiveFrom <= end && (salary.EffectiveTo == null || salary.EffectiveTo >= month))
            .ToListAsync(cancellationToken);
        IReadOnlyDictionary<Guid, ResolvedStructure> structures = await catalog.StructuresAsync(salaries.Select(salary => salary.StructureId), cancellationToken);
        decimal monthHours = (end.Day) * settings.StandardHoursPerDay;

        foreach (PayrollInput stale in existing.Where(input => !overtime.ContainsKey(input.EmployeeId)))
        {
            stale.Cancel();
        }

        foreach ((Guid employeeId, decimal hours) in overtime)
        {
            EmployeeSalary? salary = salaries.Where(row => row.EmployeeId == employeeId).MaxBy(row => row.EffectiveFrom);
            if (salary is null || !structures.TryGetValue(salary.StructureId, out ResolvedStructure? structure))
            {
                continue;
            }

            decimal gross = SalaryBreakdown.Calculate(salary.AnnualCtc, structure.Rules, new CtcRules(null, null, null, false)).Gross;
            decimal amount = Money.Rupees(hours * gross / monthHours * settings.OvertimeMultiplier);
            if (amount <= 0)
            {
                continue;
            }

            PayrollInput? current = existing.Find(input => input.EmployeeId == employeeId);
            if (current is null)
            {
                dbContext.PayrollInputs.Add(PayrollInput.Raise(employeeId, month, component, PayrollInputKind.Overtime, amount, hours, $"{hours:0.##} h overtime from timesheets", TimesheetSource, null));
            }
            else
            {
                current.Reprice(amount, hours);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return overtime.Count;
    }

    // ----- IPayrollInputs -----------------------------------------------------------------------

    public async Task<Guid> AddAsync(PayrollInputRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        PayrollInput? existing = await dbContext.PayrollInputs
            .FirstOrDefaultAsync(input => input.Source == request.Source && input.SourceId == request.SourceId && input.Status != InputStatus.Cancelled, cancellationToken);
        if (existing is not null)
        {
            if (existing.Status is InputStatus.Pending or InputStatus.Included)
            {
                existing.Reprice(request.Amount, null);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            return existing.Id;
        }

        SalaryComponent component = (await catalog.ComponentsAsync(cancellationToken)).FirstOrDefault(row => row.IsActive && row.InputKind == request.Kind)
            ?? throw new DomainRuleException("INPUT_COMPONENT", $"No active payroll component takes {request.Kind} inputs.");
        DateOnly month = new(request.Period.Year, request.Period.Month, 1);
        for (int i = 0; i < 12 && await IsLockedAsync(month, cancellationToken); i++)
        {
            month = month.AddMonths(1);
        }

        var input = PayrollInput.Raise(request.EmployeeId, month, component, request.Kind, request.Amount, null, request.Description, request.Source, request.SourceId);
        dbContext.PayrollInputs.Add(input);
        await dbContext.SaveChangesAsync(cancellationToken);
        return input.Id;
    }

    public async Task<bool> WithdrawAsync(string source, Guid sourceId, CancellationToken cancellationToken = default)
    {
        PayrollInput? input = await dbContext.PayrollInputs
            .FirstOrDefaultAsync(row => row.Source == source && row.SourceId == sourceId && (row.Status == InputStatus.Pending || row.Status == InputStatus.Included), cancellationToken);
        if (input is null)
        {
            return false;
        }

        input.Cancel();
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    // ----- Internals ----------------------------------------------------------------------------

    private async Task<bool> IsLockedAsync(DateOnly month, CancellationToken cancellationToken)
        => await dbContext.PayrollRuns.AnyAsync(run => run.Kind == PayrollRunKind.Regular && run.Period == month && (run.Status == RunStatus.Finalized || run.Status == RunStatus.Paid), cancellationToken);

    private async Task EnsureOpenAsync(DateOnly month, CancellationToken cancellationToken)
    {
        if (await IsLockedAsync(month, cancellationToken))
        {
            throw new DomainRuleException("PAYROLL_PERIOD_CLOSED", $"Payroll for {month:MMMM yyyy} is finalized; add the input to a later month.");
        }
    }

    private async Task<List<InputItem>> ItemsAsync(IReadOnlyList<PayrollInput> inputs, CancellationToken cancellationToken)
    {
        Dictionary<Guid, SalaryComponent> components = (await catalog.ComponentsAsync(cancellationToken)).ToDictionary(component => component.Id);
        Dictionary<Guid, EmployeeSummary> people = (await employees.GetManyAsync(inputs.Select(input => input.EmployeeId), cancellationToken)).ToDictionary(person => person.Id);
        return [.. inputs.Select(input => new InputItem(
            input.Id, input.EmployeeId, people.GetValueOrDefault(input.EmployeeId)?.DisplayName, people.GetValueOrDefault(input.EmployeeId)?.EmployeeCode, input.Period,
            input.ComponentId, components.GetValueOrDefault(input.ComponentId)?.Code ?? "?", components.GetValueOrDefault(input.ComponentId)?.Name ?? "?", input.Kind,
            input.Amount, input.Quantity, input.Description, input.Source, input.Status, input.RunId))];
    }
}

public sealed record DeclarationView(
    Guid? Id,
    Guid EmployeeId,
    string? EmployeeName,
    string? EmployeeCode,
    int FinancialYear,
    string FinancialYearLabel,
    TaxRegime Regime,
    DeclarationStatus Status,
    IReadOnlyList<DeclarationItem> Items,
    decimal? MonthlyRent,
    bool MetroCity,
    string? LandlordPan,
    decimal? OtherIncome,
    decimal? PreviousEmployerIncome,
    decimal? PreviousEmployerTds,
    DateTime? SubmittedDate,
    DateTime? VerifiedDate,
    string? VerifierNotes,
    IReadOnlyDictionary<string, decimal> SectionLimits,
    RegimeComparison? Comparison);

/// <summary>The year's tax on the current salary under each regime, with what is declared - to help choose.</summary>
public sealed record RegimeComparison(decimal NewRegimeTax, decimal OldRegimeTax, string Better);

public sealed record DeclarationRow(Guid Id, Guid EmployeeId, string? EmployeeName, string? EmployeeCode, TaxRegime Regime, DeclarationStatus Status, decimal Declared, DateTime? SubmittedDate);

public sealed record VerifyBody(IReadOnlyList<decimal> Accepted, string? Notes);

/// <summary>Tax declarations (Form 12BB): employees declare, payroll verifies, TDS projects from them.</summary>
internal sealed class DeclarationService(
    AppDbContext dbContext,
    PayrollCatalog catalog,
    PayrollSetupService setup,
    IEmployeeDirectory employees,
    IEmployeePayrollDetails payrollDetails,
    ICurrentUser currentUser,
    ITenantClock clock,
    TimeProvider timeProvider)
{
    public async Task<DeclarationView> MineAsync(int? year, CancellationToken cancellationToken)
    {
        Guid me = Me();
        int fy = year ?? FinancialYear.Of(await clock.TodayAsync(cancellationToken));
        TaxDeclaration? declaration = await dbContext.TaxDeclarations.AsNoTracking().FirstOrDefaultAsync(row => row.EmployeeId == me && row.FinancialYear == fy, cancellationToken);
        return await ViewAsync(me, fy, declaration, withComparison: true, cancellationToken);
    }

    public async Task<DeclarationView> SaveMineAsync(int year, DeclarationValues values, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(values);
        Guid me = Me();
        TaxDeclaration declaration = await dbContext.TaxDeclarations.FirstOrDefaultAsync(row => row.EmployeeId == me && row.FinancialYear == year, cancellationToken)
            ?? Track(TaxDeclaration.Start(me, year, values.Regime));
        declaration.Declare(values);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ViewAsync(me, year, declaration, withComparison: true, cancellationToken);
    }

    public async Task<DeclarationView> SubmitMineAsync(int year, CancellationToken cancellationToken)
    {
        Guid me = Me();
        TaxDeclaration declaration = await dbContext.TaxDeclarations.FirstOrDefaultAsync(row => row.EmployeeId == me && row.FinancialYear == year, cancellationToken)
            ?? Track(TaxDeclaration.Start(me, year, (await catalog.SettingsAsync(cancellationToken)).DefaultRegime));
        declaration.Submit(timeProvider.GetUtcNow().UtcDateTime);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ViewAsync(me, year, declaration, withComparison: true, cancellationToken);
    }

    public async Task<IReadOnlyList<DeclarationRow>> ListAsync(int? year, DeclarationStatus? status, CancellationToken cancellationToken)
    {
        int fy = year ?? FinancialYear.Of(await clock.TodayAsync(cancellationToken));
        IReadOnlyCollection<Guid>? scope = await employees.InScopeIdsAsync(Permissions.Payroll.TaxDeclarationView, cancellationToken);
        IQueryable<TaxDeclaration> rows = dbContext.TaxDeclarations.AsNoTracking().Where(row => row.FinancialYear == fy);
        if (scope is not null)
        {
            rows = rows.Where(row => scope.Contains(row.EmployeeId));
        }

        if (status is { } wanted)
        {
            rows = rows.Where(row => row.Status == wanted);
        }

        List<TaxDeclaration> list = await rows.ToListAsync(cancellationToken);
        Dictionary<Guid, EmployeeSummary> people = (await employees.GetManyAsync(list.Select(row => row.EmployeeId), cancellationToken)).ToDictionary(person => person.Id);
        return [.. list
            .Select(row => new DeclarationRow(
                row.Id, row.EmployeeId, people.GetValueOrDefault(row.EmployeeId)?.DisplayName, people.GetValueOrDefault(row.EmployeeId)?.EmployeeCode, row.Regime, row.Status,
                row.Items.Sum(item => item.Declared) + ((row.MonthlyRent ?? 0) * 12), row.SubmittedDate))
            .OrderBy(row => row.Status == DeclarationStatus.Submitted ? 0 : 1)
            .ThenBy(row => row.EmployeeName, StringComparer.CurrentCulture)];
    }

    public async Task<DeclarationView> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        TaxDeclaration declaration = await FindAsync(id, Permissions.Payroll.TaxDeclarationView, cancellationToken);
        return await ViewAsync(declaration.EmployeeId, declaration.FinancialYear, declaration, withComparison: false, cancellationToken);
    }

    public async Task<DeclarationView> VerifyAsync(Guid id, VerifyBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        TaxDeclaration declaration = await FindAsync(id, Permissions.Payroll.TaxDeclarationVerify, cancellationToken, tracked: true);
        declaration.Verify(body.Accepted ?? [], currentUser.EmployeeId, body.Notes, timeProvider.GetUtcNow().UtcDateTime);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ViewAsync(declaration.EmployeeId, declaration.FinancialYear, declaration, withComparison: false, cancellationToken);
    }

    public async Task<DeclarationView> ReopenAsync(Guid id, CancellationToken cancellationToken)
    {
        TaxDeclaration declaration = await FindAsync(id, Permissions.Payroll.TaxDeclarationVerify, cancellationToken, tracked: true);
        declaration.Reopen();
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ViewAsync(declaration.EmployeeId, declaration.FinancialYear, declaration, withComparison: false, cancellationToken);
    }

    private Guid Me() => currentUser.EmployeeId ?? throw new DomainRuleException("PAYROLL_NO_EMPLOYEE", "Your account is not linked to an employee record.");

    private TaxDeclaration Track(TaxDeclaration declaration)
    {
        dbContext.TaxDeclarations.Add(declaration);
        return declaration;
    }

    private async Task<TaxDeclaration> FindAsync(Guid id, string permission, CancellationToken cancellationToken, bool tracked = false)
    {
        IQueryable<TaxDeclaration> query = tracked ? dbContext.TaxDeclarations : dbContext.TaxDeclarations.AsNoTracking();
        TaxDeclaration declaration = await query.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(TaxDeclaration), id);
        return await employees.IsInScopeAsync(declaration.EmployeeId, permission, cancellationToken) ? declaration : throw new NotFoundException(nameof(TaxDeclaration), id);
    }

    private async Task<DeclarationView> ViewAsync(Guid employeeId, int year, TaxDeclaration? declaration, bool withComparison, CancellationToken cancellationToken)
    {
        PayrollSettings settings = await catalog.SettingsAsync(cancellationToken);
        EmployeeSummary? person = await employees.GetAsync(employeeId, cancellationToken);
        DateOnly today = await clock.TodayAsync(cancellationToken);
        IReadOnlyList<StatutoryRuleVersion> rules = await catalog.RulesInForceAsync(today, cancellationToken);
        IncomeTaxConfig? taxRule = PayrollCatalog.RulesFor(rules, null).IncomeTax?.Read<IncomeTaxConfig>();

        RegimeComparison? comparison = null;
        if (withComparison && taxRule is not null)
        {
            comparison = await CompareAsync(employeeId, declaration, settings, today, taxRule, cancellationToken);
        }

        return new DeclarationView(
            declaration?.Id, employeeId, person?.DisplayName, person?.EmployeeCode, year, FinancialYear.Label(year), declaration?.Regime ?? settings.DefaultRegime,
            declaration?.Status ?? DeclarationStatus.Draft, declaration?.Items ?? [], declaration?.MonthlyRent, declaration?.MetroCity ?? false, declaration?.LandlordPan,
            declaration?.OtherIncome, declaration?.PreviousEmployerIncome, declaration?.PreviousEmployerTds, declaration?.SubmittedDate, declaration?.VerifiedDate,
            declaration?.VerifierNotes, taxRule?.SectionLimits ?? new Dictionary<string, decimal>(), comparison);
    }

    private async Task<RegimeComparison?> CompareAsync(Guid employeeId, TaxDeclaration? declaration, PayrollSettings settings, DateOnly today, IncomeTaxConfig taxRule, CancellationToken cancellationToken)
    {
        EmployeeSalary? salary = await dbContext.EmployeeSalaries.AsNoTracking()
            .Where(row => row.EmployeeId == employeeId && row.EffectiveFrom <= today && (row.EffectiveTo == null || row.EffectiveTo >= today))
            .OrderByDescending(row => row.EffectiveFrom)
            .FirstOrDefaultAsync(cancellationToken);
        if (salary is null || (await catalog.StructuresAsync([salary.StructureId], cancellationToken)).GetValueOrDefault(salary.StructureId) is not { } structure)
        {
            return null;
        }

        string? state = (await payrollDetails.GetAsync([employeeId], reveal: false, cancellationToken)).GetValueOrDefault(employeeId)?.ProfessionalTaxStateCode ?? settings.DefaultStateCode;
        SalaryPreview month = await setup.PreviewAsync(structure, salary.AnnualCtc, state, TaxRegime.New, salary.PfEnabled, today, cancellationToken);
        decimal Line(string code) => month.Lines.FirstOrDefault(line => line.Code == code)?.Monthly ?? 0;
        decimal taxable = month.Lines.Where(line => line.Kind == ComponentKind.Earning).Sum(line => line.Monthly);

        DeclarationFacts facts = Facts(declaration);
        decimal Tax(TaxRegime regime) => TaxMath.Project(
            new TaxInput(regime, 12, 11, taxable, taxable, Line("BASIC"), Line("BASIC"), Line("HRA"), Line("HRA"), Line(StatutoryKeys.ProfessionalTax),
                Line(StatutoryKeys.ProfessionalTax), Line(StatutoryKeys.ProvidentFundEmployee), Line(StatutoryKeys.ProvidentFundEmployee), YearToDate.None, facts with { Regime = regime }),
            taxRule).AnnualTax;

        decimal newTax = Tax(TaxRegime.New);
        decimal oldTax = Tax(TaxRegime.Old);
        return new RegimeComparison(newTax, oldTax, newTax <= oldTax ? nameof(TaxRegime.New) : nameof(TaxRegime.Old));
    }

    internal static DeclarationFacts Facts(TaxDeclaration? declaration)
        => declaration is null
            ? new DeclarationFacts(TaxRegime.New, [], 0, false, 0, 0, 0)
            : new DeclarationFacts(
                declaration.Regime, [.. declaration.Claims()], declaration.MonthlyRent ?? 0, declaration.MetroCity, declaration.OtherIncome ?? 0,
                declaration.PreviousEmployerIncome ?? 0, declaration.PreviousEmployerTds ?? 0);
}
