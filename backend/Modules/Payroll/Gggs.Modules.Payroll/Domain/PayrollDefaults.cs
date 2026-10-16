using Gggs.Modules.Payroll.Contracts;

namespace Gggs.Modules.Payroll.Domain;

/// <summary>A seeded component.</summary>
public sealed record ComponentSeed(string Code, ComponentDefinition Definition, bool IsSystem);

/// <summary>
/// The components and structure a tenant starts with: a conventional Indian CTC (basic at 40%, HRA at
/// half of basic, a special allowance balancing the rest), the statutory lines payroll fills in, and
/// the input components other modules pay through. All of it is editable except what payroll relies on.
/// </summary>
public static class PayrollDefaults
{
    public const string StandardStructureCode = "STANDARD";

    public static IReadOnlyList<ComponentSeed> Components { get; } =
    [
        Earning("BASIC", "Basic", CalculationType.Percentage, 40m, "CTC", null, pf: true, esi: true, order: 10, system: false, "The base of PF, gratuity and HRA."),
        Earning("HRA", "House rent allowance", CalculationType.Percentage, 50m, "BASIC", null, pf: false, esi: true, order: 20, system: false, "Partly exempt under the old regime when rent is paid."),
        Earning("SPECIAL", "Special allowance", CalculationType.Balancing, null, null, null, pf: false, esi: true, order: 30, system: false, "Whatever remains of the CTC."),
        Input("OVERTIME", "Overtime", ComponentKind.Earning, PayrollInputKind.Overtime, taxable: true, esi: true, order: 40),
        Input("BONUS", "Bonus", ComponentKind.Earning, PayrollInputKind.Bonus, taxable: true, esi: false, order: 41),
        Input("INCENTIVE", "Incentive", ComponentKind.Earning, PayrollInputKind.Incentive, taxable: true, esi: true, order: 42),
        Input("COMMISSION", "Commission", ComponentKind.Earning, PayrollInputKind.Commission, taxable: true, esi: true, order: 43),
        Input("ARREARS", "Arrears", ComponentKind.Earning, PayrollInputKind.Arrears, taxable: true, esi: true, order: 44),
        Input("REIMBURSEMENT", "Reimbursement", ComponentKind.Reimbursement, PayrollInputKind.Reimbursement, taxable: false, esi: false, order: 50),
        Statutory(StatutoryKeys.ProvidentFundEmployer, "Employer PF", ComponentKind.EmployerContribution, 60),
        Statutory(StatutoryKeys.StateInsuranceEmployer, "Employer ESI", ComponentKind.EmployerContribution, 61),
        Statutory(StatutoryKeys.Gratuity, "Gratuity", ComponentKind.EmployerContribution, 62),
        Statutory(StatutoryKeys.LabourWelfareEmployer, "Employer labour welfare", ComponentKind.EmployerContribution, 63),
        Statutory(StatutoryKeys.ProvidentFundEmployee, "Provident fund", ComponentKind.Deduction, 80),
        Statutory(StatutoryKeys.StateInsuranceEmployee, "ESI", ComponentKind.Deduction, 81),
        Statutory(StatutoryKeys.ProfessionalTax, "Professional tax", ComponentKind.Deduction, 82),
        Statutory(StatutoryKeys.IncomeTax, "Income tax (TDS)", ComponentKind.Deduction, 83),
        Statutory(StatutoryKeys.LabourWelfareEmployee, "Labour welfare fund", ComponentKind.Deduction, 84),
        Input("RECOVERY", "Other recovery", ComponentKind.Deduction, PayrollInputKind.Deduction, taxable: false, esi: false, order: 90),
    ];

    /// <summary>The codes of the standard structure, in payslip order.</summary>
    public static IReadOnlyList<string> StandardStructure { get; } =
        ["BASIC", "HRA", "SPECIAL", StatutoryKeys.ProvidentFundEmployer, StatutoryKeys.StateInsuranceEmployer, StatutoryKeys.Gratuity];

    private static ComponentSeed Earning(string code, string name, CalculationType type, decimal? value, string? percentOf, string? formula, bool pf, bool esi, int order, bool system, string description)
        => new(code, new ComponentDefinition(name, ComponentKind.Earning, type, value, percentOf, formula, null, IsTaxable: true, pf, esi, PtApplicable: true, IsProrated: true, ShowOnPayslip: true, order, description), system);

    private static ComponentSeed Input(string code, string name, ComponentKind kind, PayrollInputKind inputKind, bool taxable, bool esi, int order)
        => new(code, new ComponentDefinition(name, kind, CalculationType.Input, null, null, null, inputKind, taxable, PfApplicable: inputKind == PayrollInputKind.Arrears, esi, PtApplicable: kind == ComponentKind.Earning, IsProrated: false, ShowOnPayslip: true, order, null), IsSystem: true);

    private static ComponentSeed Statutory(string code, string name, ComponentKind kind, int order)
        => new(code, new ComponentDefinition(name, kind, CalculationType.Statutory, null, null, null, null, IsTaxable: false, PfApplicable: false, EsiApplicable: false, PtApplicable: false, IsProrated: false, ShowOnPayslip: true, order, "Worked out by its statutory rule."), IsSystem: true);
}
