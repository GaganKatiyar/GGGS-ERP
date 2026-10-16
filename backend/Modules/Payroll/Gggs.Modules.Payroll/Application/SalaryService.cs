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
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Payroll.Application;

public sealed record SalaryHistoryItem(
    Guid Id,
    Guid StructureId,
    string StructureName,
    decimal AnnualCtc,
    string Currency,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    SalaryReason Reason,
    string Source,
    bool PfEnabled,
    string? Notes,
    decimal? ChangePercent);

public sealed record GratuityEstimate(decimal YearsOfService, decimal Amount, bool Eligible, decimal MonthlyBasic);

public sealed record EmployeeSalaryView(
    Guid EmployeeId,
    string? EmployeeName,
    string? EmployeeCode,
    SalaryHistoryItem? Current,
    SalaryPreview? Breakdown,
    IReadOnlyList<SalaryHistoryItem> History,
    GratuityEstimate? Gratuity,
    bool CanManage);

public sealed record AssignSalaryBody(Guid StructureId, decimal AnnualCtc, DateOnly EffectiveFrom, SalaryReason Reason, bool? PfEnabled, string? Currency, string? Notes);

public sealed record SalaryRow(Guid EmployeeId, string EmployeeName, string EmployeeCode, string? Designation, Guid? DepartmentId, decimal? AnnualCtc, string? StructureName, DateOnly? EffectiveFrom);

public sealed record SalaryListQuery
{
    public string? Search { get; init; }

    public bool? WithoutSalary { get; init; }

    public int? Page { get; init; }

    public int? PageSize { get; init; }
}

/// <summary>
/// Employee salaries (spec §21, §23): the CTC in force, laid out on its structure, the history of
/// revisions, and the gratuity it has earned. Salary data is sensitive and always scoped.
/// </summary>
internal sealed class SalaryService(
    AppDbContext dbContext,
    PayrollCatalog catalog,
    PayrollSetupService setup,
    IEmployeeDirectory employees,
    IEmployeePayrollDetails payrollDetails,
    IPermissionProvider permissions,
    ICurrentUser currentUser,
    ITenantClock clock) : IPayrollSalaries
{
    public async Task<PagedResult<SalaryRow>> ListAsync(SalaryListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var page = new PageRequest { Page = query.Page ?? 1, PageSize = query.PageSize ?? 50 };
        IReadOnlyCollection<Guid>? scope = await employees.InScopeIdsAsync(Permissions.Payroll.EmployeeSalaryView, cancellationToken);
        IReadOnlyList<Guid> active = await employees.ListActiveIdsAsync(null, null, cancellationToken);
        List<Guid> ids = [.. active.Where(id => scope is null || scope.Contains(id))];

        DateOnly today = await clock.TodayAsync(cancellationToken);
        Dictionary<Guid, EmployeeSalary> current = (await dbContext.EmployeeSalaries.AsNoTracking()
                .Where(salary => ids.Contains(salary.EmployeeId) && salary.EffectiveFrom <= today && (salary.EffectiveTo == null || salary.EffectiveTo >= today))
                .ToListAsync(cancellationToken))
            .GroupBy(salary => salary.EmployeeId)
            .ToDictionary(group => group.Key, group => group.MaxBy(salary => salary.EffectiveFrom)!);
        Dictionary<Guid, string> structures = await dbContext.SalaryStructures.AsNoTracking().ToDictionaryAsync(structure => structure.Id, structure => structure.Name, cancellationToken);

        List<EmployeeSummary> people = [.. (await employees.GetManyAsync(ids, cancellationToken)).OrderBy(person => person.DisplayName, StringComparer.CurrentCulture)];
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            string term = query.Search.Trim();
            people = [.. people.Where(person => person.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase) || person.EmployeeCode.Contains(term, StringComparison.OrdinalIgnoreCase))];
        }

        if (query.WithoutSalary == true)
        {
            people = [.. people.Where(person => !current.ContainsKey(person.Id))];
        }

        List<SalaryRow> rows = [.. people.Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).Select(person =>
        {
            EmployeeSalary? salary = current.GetValueOrDefault(person.Id);
            return new SalaryRow(person.Id, person.DisplayName, person.EmployeeCode, person.Designation, person.DepartmentId, salary?.AnnualCtc,
                salary is null ? null : structures.GetValueOrDefault(salary.StructureId), salary?.EffectiveFrom);
        })];
        return PagedResult.From<SalaryRow>(rows, page, people.Count);
    }

    public async Task<EmployeeSalaryView> GetAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        if (!await employees.IsInScopeAsync(employeeId, Permissions.Payroll.EmployeeSalaryView, cancellationToken))
        {
            throw new NotFoundException("Employee", employeeId);
        }

        bool canManage = (await permissions.GetCurrentAsync(cancellationToken)).Has(Permissions.Payroll.EmployeeSalaryManage)
            && await employees.IsInScopeAsync(employeeId, Permissions.Payroll.EmployeeSalaryManage, cancellationToken);
        return await ViewAsync(employeeId, canManage, cancellationToken);
    }

    /// <summary>One's own salary, for self-service total rewards.</summary>
    public Task<EmployeeSalaryView> MineAsync(CancellationToken cancellationToken)
        => ViewAsync(currentUser.EmployeeId ?? throw new DomainRuleException("PAYROLL_NO_EMPLOYEE", "Your account is not linked to an employee record."), canManage: false, cancellationToken);

    public async Task<EmployeeSalaryView> AssignAsync(Guid employeeId, AssignSalaryBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!await employees.IsInScopeAsync(employeeId, Permissions.Payroll.EmployeeSalaryManage, cancellationToken))
        {
            throw new NotFoundException("Employee", employeeId);
        }

        await RecordAsync(employeeId, body.StructureId, body.AnnualCtc, body.EffectiveFrom, body.Reason, "Manual", null, body.PfEnabled, body.Currency, body.Notes, cancellationToken);
        return await ViewAsync(employeeId, canManage: true, cancellationToken);
    }

    // ----- IPayrollSalaries ---------------------------------------------------------------------

    public async Task<IReadOnlyDictionary<Guid, SalaryInfo>> CurrentAsync(IEnumerable<Guid> employeeIds, DateOnly? day, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(employeeIds);
        List<Guid> ids = [.. employeeIds.Distinct()];
        DateOnly on = day ?? await clock.TodayAsync(cancellationToken);
        List<EmployeeSalary> salaries = [.. (await dbContext.EmployeeSalaries.AsNoTracking()
                .Where(salary => ids.Contains(salary.EmployeeId) && salary.EffectiveFrom <= on && (salary.EffectiveTo == null || salary.EffectiveTo >= on))
                .ToListAsync(cancellationToken))
            .GroupBy(salary => salary.EmployeeId)
            .Select(group => group.MaxBy(salary => salary.EffectiveFrom)!)];
        if (salaries.Count == 0)
        {
            return new Dictionary<Guid, SalaryInfo>();
        }

        IReadOnlyDictionary<Guid, ResolvedStructure> structures = await catalog.StructuresAsync(salaries.Select(salary => salary.StructureId), cancellationToken);
        IReadOnlyList<StatutoryRuleVersion> rules = await catalog.RulesInForceAsync(on, cancellationToken);
        RulesInForce national = PayrollCatalog.RulesFor(rules, null);
        var ctcRules = new CtcRules(national.ProvidentFund?.Read<ProvidentFundConfig>(), national.StateInsurance?.Read<StateInsuranceConfig>(), national.Gratuity?.Read<GratuityConfig>(), true);

        var result = new Dictionary<Guid, SalaryInfo>();
        foreach (EmployeeSalary salary in salaries)
        {
            if (!structures.TryGetValue(salary.StructureId, out ResolvedStructure? structure))
            {
                continue;
            }

            Breakdown breakdown = SalaryBreakdown.Calculate(salary.AnnualCtc, structure.Rules, ctcRules with { PfEnabled = salary.PfEnabled });
            result[salary.EmployeeId] = new SalaryInfo(
                salary.Id, salary.EmployeeId, salary.AnnualCtc, salary.Currency, salary.EffectiveFrom, salary.StructureId, structure.Structure.Name,
                breakdown.Gross, breakdown.Amount("BASIC"),
                [.. breakdown.Lines.Select(line => new SalaryLine(line.Component.Code, line.Component.Name, line.Component.Kind.ToString(), line.Monthly, line.Annual))]);
        }

        return result;
    }

    public async Task<Guid> ReviseAsync(SalaryRevisionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EmployeeSalary? current = await dbContext.EmployeeSalaries.AsNoTracking()
            .Where(salary => salary.EmployeeId == request.EmployeeId && salary.EffectiveFrom <= request.EffectiveFrom)
            .OrderByDescending(salary => salary.EffectiveFrom)
            .FirstOrDefaultAsync(cancellationToken);
        Guid structureId = request.StructureId ?? current?.StructureId
            ?? await dbContext.SalaryStructures.Where(structure => structure.Code == PayrollDefaults.StandardStructureCode).Select(structure => structure.Id).FirstOrDefaultAsync(cancellationToken);
        if (structureId == Guid.Empty)
        {
            throw new DomainRuleException("SALARY_STRUCTURE", "There is no salary structure to put the revision on.");
        }

        SalaryReason reason = Enum.TryParse(request.Reason, ignoreCase: true, out SalaryReason parsed) ? parsed : SalaryReason.Revision;
        return await RecordAsync(request.EmployeeId, structureId, request.AnnualCtc, request.EffectiveFrom, reason, request.Source, request.SourceId, current?.PfEnabled, current?.Currency, null, cancellationToken);
    }

    // ----- Internals ----------------------------------------------------------------------------

    private async Task<Guid> RecordAsync(Guid employeeId, Guid structureId, decimal annualCtc, DateOnly effectiveFrom, SalaryReason reason, string source, Guid? sourceId, bool? pfEnabled, string? currency, string? notes, CancellationToken cancellationToken)
    {
        if (await employees.GetAsync(employeeId, cancellationToken) is not { } person)
        {
            throw new NotFoundException("Employee", employeeId);
        }

        if (person.JoiningDate is { } joined && effectiveFrom < joined)
        {
            throw new DomainRuleException("SALARY_DATE", $"A salary cannot start before the employee joined on {joined:d MMM yyyy}.");
        }

        SalaryStructure structure = await dbContext.SalaryStructures.AsNoTracking().FirstOrDefaultAsync(row => row.Id == structureId && row.IsActive, cancellationToken)
            ?? throw new DomainRuleException("SALARY_STRUCTURE", "Choose an active salary structure.");

        if (await PaidThroughAsync(employeeId, cancellationToken) is { } paidThrough && effectiveFrom <= paidThrough)
        {
            throw new DomainRuleException(
                "SALARY_PAID",
                $"Payroll has been finalized through {paidThrough:d MMM yyyy}; the new salary must start after it. Pay the difference as arrears.");
        }

        List<EmployeeSalary> existing = await dbContext.EmployeeSalaries.Where(salary => salary.EmployeeId == employeeId).OrderBy(salary => salary.EffectiveFrom).ToListAsync(cancellationToken);
        if (existing.Exists(salary => salary.EffectiveFrom == effectiveFrom))
        {
            throw new DomainRuleException("SALARY_DATE", $"A salary already starts on {effectiveFrom:d MMM yyyy}.");
        }

        if (existing.Exists(salary => salary.EffectiveFrom > effectiveFrom))
        {
            throw new DomainRuleException("SALARY_DATE", "A later salary is already recorded; record changes in date order.");
        }

        PayrollSettings settings = await catalog.SettingsAsync(cancellationToken);
        EmployeeSalary? previous = existing.LastOrDefault();
        previous?.EndBefore(effectiveFrom);

        var salary = EmployeeSalary.Assign(
            employeeId, structure.Id, annualCtc, currency ?? previous?.Currency ?? settings.DefaultCurrency, effectiveFrom, reason, source, sourceId,
            pfEnabled ?? previous?.PfEnabled ?? true, notes);
        dbContext.EmployeeSalaries.Add(salary);
        await dbContext.SaveChangesAsync(cancellationToken);
        return salary.Id;
    }

    /// <summary>The last day of the latest finalized payroll that paid this employee.</summary>
    private async Task<DateOnly?> PaidThroughAsync(Guid employeeId, CancellationToken cancellationToken)
        => await dbContext.Payslips.AsNoTracking()
            .Where(payslip => payslip.EmployeeId == employeeId)
            .OrderByDescending(payslip => payslip.Period)
            .Select(payslip => (DateOnly?)payslip.Period)
            .FirstOrDefaultAsync(cancellationToken) is { } period
            ? period.AddMonths(1).AddDays(-1)
            : null;

    private async Task<EmployeeSalaryView> ViewAsync(Guid employeeId, bool canManage, CancellationToken cancellationToken)
    {
        EmployeeSummary? person = await employees.GetAsync(employeeId, cancellationToken);
        DateOnly today = await clock.TodayAsync(cancellationToken);
        List<EmployeeSalary> salaries = await dbContext.EmployeeSalaries.AsNoTracking().Where(salary => salary.EmployeeId == employeeId).OrderByDescending(salary => salary.EffectiveFrom).ToListAsync(cancellationToken);
        IReadOnlyDictionary<Guid, ResolvedStructure> structures = await catalog.StructuresAsync(salaries.Select(salary => salary.StructureId), cancellationToken);

        var history = salaries.Select((salary, index) =>
        {
            EmployeeSalary? before = index + 1 < salaries.Count ? salaries[index + 1] : null;
            return new SalaryHistoryItem(
                salary.Id, salary.StructureId, structures.GetValueOrDefault(salary.StructureId)?.Structure.Name ?? "?", salary.AnnualCtc, salary.Currency,
                salary.EffectiveFrom, salary.EffectiveTo, salary.Reason, salary.Source, salary.PfEnabled, salary.Notes,
                before is null || before.AnnualCtc == 0 ? null : Math.Round((salary.AnnualCtc - before.AnnualCtc) * 100 / before.AnnualCtc, 1));
        }).ToList();

        EmployeeSalary? current = salaries.FirstOrDefault(salary => salary.EffectiveFrom <= today && (salary.EffectiveTo is null || salary.EffectiveTo >= today))
            ?? salaries.LastOrDefault(salary => salary.EffectiveFrom > today);
        SalaryPreview? breakdown = null;
        GratuityEstimate? gratuity = null;
        if (current is not null && structures.TryGetValue(current.StructureId, out ResolvedStructure? structure))
        {
            PayrollSettings settings = await catalog.SettingsAsync(cancellationToken);
            PayrollIdentity? identity = (await payrollDetails.GetAsync([employeeId], reveal: false, cancellationToken)).GetValueOrDefault(employeeId);
            TaxRegime regime = await dbContext.TaxDeclarations.AsNoTracking()
                .Where(declaration => declaration.EmployeeId == employeeId && declaration.FinancialYear == Domain.FinancialYear.Of(today))
                .Select(declaration => (TaxRegime?)declaration.Regime)
                .FirstOrDefaultAsync(cancellationToken) ?? settings.DefaultRegime;
            breakdown = await setup.PreviewAsync(structure, current.AnnualCtc, identity?.ProfessionalTaxStateCode ?? settings.DefaultStateCode, regime, current.PfEnabled, today, cancellationToken);

            if (person?.JoiningDate is { } joined && PayrollCatalog.RulesFor(await catalog.RulesInForceAsync(today, cancellationToken), null).Gratuity?.Read<GratuityConfig>() is { } gratuityRule)
            {
                decimal basic = breakdown.Lines.FirstOrDefault(line => line.Code == "BASIC")?.Monthly ?? 0;
                (decimal years, decimal amount, bool eligible) = StatutoryMath.Gratuity(basic, joined, person.ExitDate ?? today, gratuityRule);
                gratuity = new GratuityEstimate(years, amount, eligible, basic);
            }
        }

        return new EmployeeSalaryView(
            employeeId, person?.DisplayName, person?.EmployeeCode, current is null ? null : history.First(item => item.Id == current.Id), breakdown, history, gratuity, canManage);
    }
}
