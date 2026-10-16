using Gggs.Data;
using Gggs.Modules.Compliance.Contracts;
using Gggs.Modules.Payroll.Domain;
using Gggs.Modules.Payroll.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Payroll.Application;

/// <summary>A structure with its components resolved, ready to calculate with.</summary>
public sealed record ResolvedStructure(SalaryStructure Structure, IReadOnlyList<ComponentRule> Rules);

/// <summary>Loads what every payroll calculation reads: components, structures, settings and the rules in force.</summary>
internal sealed class PayrollCatalog(AppDbContext dbContext, IStatutoryRuleBook ruleBook)
{
    private List<SalaryComponent>? _components;

    public async Task<IReadOnlyList<SalaryComponent>> ComponentsAsync(CancellationToken cancellationToken)
        => _components ??= await dbContext.SalaryComponents.AsNoTracking().OrderBy(component => component.DisplayOrder).ThenBy(component => component.Code).ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, ResolvedStructure>> StructuresAsync(IEnumerable<Guid>? structureIds, CancellationToken cancellationToken)
    {
        IQueryable<SalaryStructure> query = dbContext.SalaryStructures.AsNoTracking();
        if (structureIds is not null)
        {
            List<Guid> ids = [.. structureIds.Distinct()];
            query = query.Where(structure => ids.Contains(structure.Id));
        }

        List<SalaryStructure> structures = await query.ToListAsync(cancellationToken);
        Dictionary<Guid, SalaryComponent> components = (await ComponentsAsync(cancellationToken)).ToDictionary(component => component.Id);
        return structures.ToDictionary(structure => structure.Id, structure => Resolve(structure, components));
    }

    public static ResolvedStructure Resolve(SalaryStructure structure, IReadOnlyDictionary<Guid, SalaryComponent> components)
    {
        ArgumentNullException.ThrowIfNull(structure);
        ArgumentNullException.ThrowIfNull(components);
        return new ResolvedStructure(
            structure,
            [.. structure.Lines
                .Where(line => components.ContainsKey(line.ComponentId))
                .Select(line => ComponentRule.From(components[line.ComponentId], line))]);
    }

    public async Task<PayrollSettings> SettingsAsync(CancellationToken cancellationToken)
        => await dbContext.PayrollSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken) ?? PayrollSettings.Defaults();

    public Task<IReadOnlyList<StatutoryRuleVersion>> RulesInForceAsync(DateOnly day, CancellationToken cancellationToken) => ruleBook.InForceAsync(day, cancellationToken);

    /// <summary>The versions that apply to someone working in <paramref name="stateCode"/>: the state's own rule first, else the country's.</summary>
    public static RulesInForce RulesFor(IReadOnlyList<StatutoryRuleVersion> inForce, string? stateCode)
    {
        ArgumentNullException.ThrowIfNull(inForce);
        StatutoryRuleVersion? Pick(StatutoryRuleType type, bool stateOnly)
        {
            List<StatutoryRuleVersion> candidates = [.. inForce.Where(rule => rule.Type == type)];
            StatutoryRuleVersion? state = stateCode is null ? null : candidates.Where(rule => string.Equals(rule.StateCode, stateCode, StringComparison.OrdinalIgnoreCase)).MaxBy(rule => rule.Version);
            return state ?? (stateOnly ? null : candidates.Where(rule => rule.StateCode is null).MaxBy(rule => rule.Version));
        }

        return new RulesInForce(
            Pick(StatutoryRuleType.ProvidentFund, false),
            Pick(StatutoryRuleType.StateInsurance, false),
            Pick(StatutoryRuleType.ProfessionalTax, true),
            Pick(StatutoryRuleType.LabourWelfareFund, true),
            Pick(StatutoryRuleType.IncomeTax, false),
            Pick(StatutoryRuleType.Gratuity, false));
    }
}
