using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.Data;
using Gggs.Modules.Compliance.Contracts;
using Gggs.Modules.Payroll.Contracts;
using Gggs.Modules.Payroll.Domain;
using Gggs.Modules.Payroll.Infrastructure;
using Gggs.Modules.Platform.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Payroll.Application;

public sealed record ComponentItem(
    Guid Id,
    string Code,
    string Name,
    ComponentKind Kind,
    CalculationType CalculationType,
    decimal? Value,
    string? PercentOf,
    string? Formula,
    PayrollInputKind? InputKind,
    bool IsTaxable,
    bool PfApplicable,
    bool EsiApplicable,
    bool PtApplicable,
    bool IsProrated,
    bool ShowOnPayslip,
    int DisplayOrder,
    bool IsSystem,
    bool IsActive,
    string? Description);

public sealed record ComponentBody(
    string? Code,
    string Name,
    ComponentKind Kind,
    CalculationType CalculationType,
    decimal? Value,
    string? PercentOf,
    string? Formula,
    PayrollInputKind? InputKind,
    bool IsTaxable,
    bool PfApplicable,
    bool EsiApplicable,
    bool PtApplicable,
    bool IsProrated,
    bool ShowOnPayslip,
    int DisplayOrder,
    string? Description)
{
    public ComponentDefinition ToDefinition() => new(
        Name, Kind, CalculationType, Value, PercentOf, Formula, InputKind, IsTaxable, PfApplicable, EsiApplicable, PtApplicable, IsProrated, ShowOnPayslip,
        DisplayOrder, Description);
}

public sealed record StructureLineItem(Guid ComponentId, string Code, string Name, ComponentKind Kind, CalculationType CalculationType, decimal? Value, string? PercentOf, string? Formula, bool Overridden);

public sealed record StructureItem(Guid Id, string Code, string Name, string? Description, bool IsActive, IReadOnlyList<StructureLineItem> Lines, int EmployeeCount);

public sealed record StructureBody(string? Code, string Name, string? Description, IReadOnlyList<StructureLine> Lines);

public sealed record PreviewBody(decimal AnnualCtc, string? StateCode, TaxRegime? Regime, bool PfEnabled = true);

public sealed record PreviewLine(string Code, string Name, ComponentKind Kind, decimal Monthly, decimal Annual);

/// <summary>A CTC laid out on a structure, with the month's estimated deductions and take-home.</summary>
public sealed record SalaryPreview(
    decimal AnnualCtc,
    decimal MonthlyCtc,
    decimal MonthlyGross,
    decimal MonthlyDeductions,
    decimal MonthlyTakeHome,
    decimal MonthlyEmployerContributions,
    IReadOnlyList<PreviewLine> Lines,
    TaxComputation? Tax,
    IReadOnlyList<string> Issues);

/// <summary>Payroll set-up (spec §21): components, structures, the CTC preview, and the payroll rules.</summary>
internal sealed class PayrollSetupService(AppDbContext dbContext, PayrollCatalog catalog, ITenantClock clock)
{
    private static readonly string[] FormulaVariables = ["CTC"];

    // ----- Components ---------------------------------------------------------------------------

    public async Task<IReadOnlyList<ComponentItem>> ComponentsAsync(bool includeInactive, CancellationToken cancellationToken)
        => [.. (await catalog.ComponentsAsync(cancellationToken)).Where(component => includeInactive || component.IsActive).Select(Item)];

    public async Task<ComponentItem> SaveComponentAsync(Guid? id, ComponentBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        List<SalaryComponent> all = await dbContext.SalaryComponents.ToListAsync(cancellationToken);
        if (body.CalculationType == CalculationType.Statutory)
        {
            throw new DomainRuleException("COMPONENT_STATUTORY", "Statutory components are provided by payroll; they cannot be added.");
        }

        if (body.CalculationType == CalculationType.Formula && body.Formula is { } formula)
        {
            Formula.Validate(formula, [.. all.Select(component => component.Code), .. FormulaVariables]);
        }

        if (body.CalculationType == CalculationType.Percentage && body.PercentOf is { } basis
            && !FormulaVariables.Contains(basis.Trim(), StringComparer.OrdinalIgnoreCase)
            && !all.Exists(component => string.Equals(component.Code, basis.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainRuleException("COMPONENT_VALUE", $"{basis} is not a component code or CTC.");
        }

        SalaryComponent component;
        if (id is { } existing)
        {
            component = all.Find(row => row.Id == existing) ?? throw new NotFoundException(nameof(SalaryComponent), existing);
            component.Update(body.ToDefinition());
        }
        else
        {
            string code = (body.Code ?? string.Empty).Trim().ToUpperInvariant();
            if (all.Exists(row => row.Code == code))
            {
                throw new DomainRuleException("COMPONENT_CODE", $"A component {code} already exists.");
            }

            component = SalaryComponent.Create(code, body.ToDefinition());
            dbContext.SalaryComponents.Add(component);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return Item(component);
    }

    public async Task SetComponentActiveAsync(Guid id, bool active, CancellationToken cancellationToken)
    {
        SalaryComponent component = await dbContext.SalaryComponents.FirstOrDefaultAsync(row => row.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(SalaryComponent), id);
        if (active)
        {
            component.Activate();
        }
        else
        {
            component.Deactivate();
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    // ----- Structures ---------------------------------------------------------------------------

    public async Task<IReadOnlyList<StructureItem>> StructuresAsync(CancellationToken cancellationToken)
    {
        List<SalaryStructure> structures = await dbContext.SalaryStructures.AsNoTracking().OrderBy(structure => structure.Name).ToListAsync(cancellationToken);
        Dictionary<Guid, SalaryComponent> components = (await catalog.ComponentsAsync(cancellationToken)).ToDictionary(component => component.Id);
        DateOnly today = await clock.TodayAsync(cancellationToken);
        Dictionary<Guid, int> counts = await dbContext.EmployeeSalaries.AsNoTracking()
            .Where(salary => salary.EffectiveFrom <= today && (salary.EffectiveTo == null || salary.EffectiveTo >= today))
            .GroupBy(salary => salary.StructureId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Key, row => row.Count, cancellationToken);
        return [.. structures.Select(structure => StructureView(structure, components, counts.GetValueOrDefault(structure.Id)))];
    }

    public async Task<StructureItem> SaveStructureAsync(Guid? id, StructureBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        Dictionary<Guid, SalaryComponent> components = (await catalog.ComponentsAsync(cancellationToken)).ToDictionary(component => component.Id);
        foreach (StructureLine line in body.Lines ?? [])
        {
            if (!components.TryGetValue(line.ComponentId, out SalaryComponent? component))
            {
                throw new DomainRuleException("STRUCTURE_LINES", "A structure line names a component that does not exist.");
            }

            if (component.Kind is ComponentKind.Deduction or ComponentKind.Reimbursement || component.CalculationType == CalculationType.Input)
            {
                throw new DomainRuleException("STRUCTURE_LINES", $"{component.Name} is not part of a CTC: deductions and inputs are worked out each month.");
            }
        }

        SalaryStructure structure;
        if (id is { } existing)
        {
            structure = await dbContext.SalaryStructures.FirstOrDefaultAsync(row => row.Id == existing, cancellationToken)
                ?? throw new NotFoundException(nameof(SalaryStructure), existing);
            structure.Update(body.Name, body.Description, body.Lines ?? []);
        }
        else
        {
            string code = (body.Code ?? string.Empty).Trim().ToUpperInvariant();
            if (code.Length == 0 || await dbContext.SalaryStructures.AnyAsync(row => row.Code == code, cancellationToken))
            {
                throw new DomainRuleException("STRUCTURE_CODE", "Give the structure a code no other structure uses.");
            }

            structure = SalaryStructure.Create(code, body.Name, body.Description, body.Lines ?? []);
            dbContext.SalaryStructures.Add(structure);
        }

        // A trial layout catches loops and a CTC the components cannot fit before anyone is paid on it.
        ResolvedStructure resolved = PayrollCatalog.Resolve(structure, components);
        Breakdown trial = SalaryBreakdown.Calculate(1_200_000m, resolved.Rules, new CtcRules(null, null, null, false));
        if (trial.Errors.Count > 0)
        {
            throw new DomainRuleException("STRUCTURE_INVALID", trial.Errors[0]);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return StructureView(structure, components, 0);
    }

    public async Task SetStructureActiveAsync(Guid id, bool active, CancellationToken cancellationToken)
    {
        SalaryStructure structure = await dbContext.SalaryStructures.FirstOrDefaultAsync(row => row.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(SalaryStructure), id);
        if (active)
        {
            structure.Activate();
        }
        else
        {
            structure.Deactivate();
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>A full month on the structure: the breakdown, the statutory deductions and the take-home.</summary>
    public async Task<SalaryPreview> PreviewAsync(Guid structureId, PreviewBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        ResolvedStructure structure = (await catalog.StructuresAsync([structureId], cancellationToken)).GetValueOrDefault(structureId)
            ?? throw new NotFoundException(nameof(SalaryStructure), structureId);
        PayrollSettings settings = await catalog.SettingsAsync(cancellationToken);
        DateOnly today = await clock.TodayAsync(cancellationToken);
        return await PreviewAsync(structure, body.AnnualCtc, body.StateCode ?? settings.DefaultStateCode, body.Regime ?? settings.DefaultRegime, body.PfEnabled, today, cancellationToken);
    }

    internal async Task<SalaryPreview> PreviewAsync(ResolvedStructure structure, decimal annualCtc, string? stateCode, TaxRegime regime, bool pfEnabled, DateOnly today, CancellationToken cancellationToken)
    {
        if (annualCtc <= 0)
        {
            throw new DomainRuleException("SALARY_CTC", "Enter an annual CTC to preview.");
        }

        var month = new DateOnly(today.Year, today.Month, 1);
        IReadOnlyList<StatutoryRuleVersion> rules = await catalog.RulesInForceAsync(month, cancellationToken);
        PayResult result = PayCalculator.Calculate(new PayContext(
            Guid.Empty, "PREVIEW", "Preview", null, null, month, month.AddMonths(1).AddDays(-1), ProrationBasis.CalendarDays, null, null,
            [new SalarySegment(Guid.Empty, annualCtc, structure.Rules, pfEnabled, month, null)], 0, [], [],
            PayrollCatalog.RulesFor(rules, stateCode), 30, stateCode, new DeclarationFacts(regime, [], 0, false, 0, 0, 0), regime, YearToDate.None,
            HasBankAccount: true, HasPan: true, HasUan: true));

        // Annualise the month as if every month were the same; inputs and one-offs are not in a preview.
        var lines = result.Lines.Select(line => new PreviewLine(line.Code, line.Name, line.Kind, line.Amount, line.Amount * 12)).ToList();
        return new SalaryPreview(
            annualCtc, Math.Round(annualCtc / 12, 2), result.Gross, result.Deductions, result.Net, result.EmployerContributions, lines, result.Tax,
            [.. result.Issues.Where(issue => !issue.StartsWith("No paid days", StringComparison.Ordinal))]);
    }

    // ----- Settings -----------------------------------------------------------------------------

    public async Task<PayrollSettingsValues> SettingsAsync(CancellationToken cancellationToken) => (await catalog.SettingsAsync(cancellationToken)).ToValues();

    public async Task<PayrollSettingsValues> SaveSettingsAsync(PayrollSettingsValues values, CancellationToken cancellationToken)
    {
        PayrollSettings settings = await dbContext.PayrollSettings.FirstOrDefaultAsync(cancellationToken) ?? Track(PayrollSettings.Defaults());
        settings.Update(values);
        await dbContext.SaveChangesAsync(cancellationToken);
        return settings.ToValues();
    }

    private PayrollSettings Track(PayrollSettings settings)
    {
        dbContext.PayrollSettings.Add(settings);
        return settings;
    }

    private static ComponentItem Item(SalaryComponent component) => new(
        component.Id, component.Code, component.Name, component.Kind, component.CalculationType, component.Value, component.PercentOf, component.Formula,
        component.InputKind, component.IsTaxable, component.PfApplicable, component.EsiApplicable, component.PtApplicable, component.IsProrated,
        component.ShowOnPayslip, component.DisplayOrder, component.IsSystem, component.IsActive, component.Description);

    private static StructureItem StructureView(SalaryStructure structure, Dictionary<Guid, SalaryComponent> components, int employees)
        => new(
            structure.Id, structure.Code, structure.Name, structure.Description, structure.IsActive,
            [.. structure.Lines.Where(line => components.ContainsKey(line.ComponentId)).Select(line =>
            {
                SalaryComponent component = components[line.ComponentId];
                ComponentRule rule = ComponentRule.From(component, line);
                return new StructureLineItem(
                    component.Id, component.Code, component.Name, component.Kind, rule.Type, rule.Value, rule.PercentOf, rule.Formula,
                    line.CalculationType is not null || line.Value is not null || line.PercentOf is not null || line.Formula is not null);
            })],
            employees);
}

/// <summary>Seeds a tenant's payroll: the default components, the standard structure and the payroll rules.</summary>
public sealed class PayrollSeeder(AppDbContext dbContext)
{
    public async Task<int> SeedCurrentTenantAsync(CancellationToken cancellationToken = default)
    {
        List<SalaryComponent> components = await dbContext.SalaryComponents
            .IgnoreQueryFilters([Gggs.BuildingBlocks.Persistence.QueryFilters.SoftDelete])
            .ToListAsync(cancellationToken);
        int added = 0;
        foreach (ComponentSeed seed in PayrollDefaults.Components.Where(seed => !components.Exists(component => component.Code == seed.Code)))
        {
            var component = SalaryComponent.Create(seed.Code, seed.Definition, seed.IsSystem);
            dbContext.SalaryComponents.Add(component);
            components.Add(component);
            added++;
        }

        if (!await dbContext.SalaryStructures.IgnoreQueryFilters([Gggs.BuildingBlocks.Persistence.QueryFilters.SoftDelete]).AnyAsync(structure => structure.Code == PayrollDefaults.StandardStructureCode, cancellationToken))
        {
            dbContext.SalaryStructures.Add(SalaryStructure.Create(
                PayrollDefaults.StandardStructureCode,
                "Standard",
                "Basic 40% of CTC, HRA half of basic, employer PF, ESI and gratuity inside the CTC, and a special allowance for the rest.",
                [.. PayrollDefaults.StandardStructure.Select(code => new StructureLine(components.First(component => component.Code == code).Id, null, null, null, null))]));
            added++;
        }

        if (!await dbContext.PayrollSettings.AnyAsync(cancellationToken))
        {
            dbContext.PayrollSettings.Add(PayrollSettings.Defaults());
            added++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return added;
    }
}

internal sealed class SeedPayrollOnTenantProvisioned(PayrollSeeder seeder) : IIntegrationEventConsumer<TenantProvisionedIntegrationEvent>
{
    public Task HandleAsync(TenantProvisionedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        => seeder.SeedCurrentTenantAsync(cancellationToken);
}
