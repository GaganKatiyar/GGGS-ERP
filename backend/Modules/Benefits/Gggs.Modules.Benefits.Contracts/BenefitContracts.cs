namespace Gggs.Modules.Benefits.Contracts;

/// <summary>
/// One active enrollment's monthly cost (spec §24): the employee's share is deducted in payroll,
/// the employer's share is shown on the payslip and counted in total rewards.
/// </summary>
public sealed record BenefitCharge(
    Guid EmployeeId,
    Guid EnrollmentId,
    string PlanCode,
    string PlanName,
    string PlanType,
    decimal EmployeeAmount,
    decimal EmployerAmount,
    bool EmployeeShareTaxDeductible);

/// <summary>Benefit costs for payroll (ADR-12).</summary>
public interface IBenefitCharges
{
    /// <summary>Monthly charges for enrollments active during the period, prorated to the days they were active.</summary>
    Task<IReadOnlyList<BenefitCharge>> ForPeriodAsync(IEnumerable<Guid> employeeIds, DateOnly periodStart, DateOnly periodEnd, CancellationToken cancellationToken = default);
}
